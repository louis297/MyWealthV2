using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;

namespace MyWealthV2.Simulator.Bank;

/// <summary>Deposit matching R43–R45: the statement feed the platform's bank feed adapter reads. Withdrawals R49–R51:
/// the payments the platform's bank payments adapter sends.</summary>
public static class BankPartyEndpoints
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 100;

    public static RouteGroupBuilder MapBankPartyEndpoints(this RouteGroupBuilder party)
    {
        party.MapGet("/bank/accounts/{accountNumber}/statement-lines", GetStatementLinesAsync);
        party.MapPost("/bank/accounts/{accountNumber}/payments", PayAsync);
        return party;
    }

    private static async Task<IResult> GetStatementLinesAsync(
        string accountNumber,
        string? after,
        int? limit,
        SimulatorDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var world = await db.Worlds.AsNoTracking().SingleAsync(cancellationToken);
        if (!BankFeedCursor.TryRead(after, world.WorldId, out var afterPlace))
        {
            return Results.BadRequest();
        }

        var account = await db.BankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.AccountNumber == accountNumber, cancellationToken);
        if (account is null)
        {
            return Results.NotFound();
        }

        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        await PlaceAvailableLinesAsync(db, account.Id, world.Now(time.GetUtcNow()), cancellationToken);

        var rows = await (
                from place in db.BankFeedPlaces.AsNoTracking()
                join line in db.BankLines.AsNoTracking() on place.BankLineId equals line.Id
                where place.BankAccountId == account.Id && place.Place > afterPlace
                orderby place.Place
                select new
                {
                    place.Place,
                    line,
                    ReturnOf = db.BankLines.Where(returned => returned.Id == line.ReturnOfLineId)
                        .Select(returned => returned.OutsideId)
                        .FirstOrDefault()
                })
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        var page = rows.Take(take).ToList();
        var items = page.Select(row => new BankFeedItem(
            row.line.OutsideId,
            row.line.ValueDate,
            row.line.Direction == BankLine.CreditDirection ? "Credit" : "Debit",
            row.line.Amount,
            account.Currency.Trim(),
            row.line.PayerName,
            row.line.PayerAccount,
            row.line.Reference,
            row.line.EndToEndId,
            row.ReturnOf,
            row.line.ReturnReason)).ToList();

        // The place of the last item; with no item, the request's place in this world (0: before the first place).
        var cursor = BankFeedCursor.Format(world.WorldId, page.Count > 0 ? page[^1].Place : afterPlace);
        return Results.Ok(new BankFeedResponse(items, cursor, rows.Count > take));
    }

    /// <summary>
    /// Withdrawals R49–R51, R53: one payment from a trust account. Unknown account → 404; a body outside the field
    /// rules → 400; the same instruction id and body again → the first answer; the same id with another body → 409.
    /// The payment mode decides the answer (R52); an executed payment is a debit line in the feed at once.
    /// </summary>
    private static async Task<IResult> PayAsync(
        string accountNumber,
        PaymentRequest body,
        SimulatorDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var account = await db.BankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.AccountNumber == accountNumber, cancellationToken);
        if (account is null)
        {
            return Results.NotFound();
        }

        if (!Valid(body, account))
        {
            return Results.BadRequest();
        }

        var instructionId = body.InstructionId!;
        var hash = Hash(body);
        var known = await db.BankPayments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BankAccountId == account.Id && row.InstructionId == instructionId, cancellationToken);
        if (known is not null)
        {
            return Replay(known, hash);
        }

        var world = await db.Worlds.SingleAsync(cancellationToken);
        var simulatorNow = world.Now(time.GetUtcNow());
        var payment = BankPayment.Receive(
            world.TakePaymentId(),
            account.Id,
            instructionId,
            hash,
            body.Amount!.Value,
            BankRules.Text(body.Reference),
            body.Payee!.AccountNumber!.Trim(),
            body.Payee.Name!.Trim(),
            (BankPaymentMode)world.BankPaymentMode,
            world.BankPaymentRejectReason,
            simulatorNow);
        var line = payment.Status == BankPaymentStatus.Executed
            ? BankLine.Payment(world.TakeLineOutsideId(), payment, DateOnly.FromDateTime(simulatorNow.UtcDateTime), simulatorNow)
            : null;
        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                db.BankPayments.Add(payment);
                if (line is not null)
                {
                    db.BankLines.Add(line);
                }

                await db.SaveChangesAsync(cancellationToken);
                if (line is not null)
                {
                    payment.Executed(line.Id);
                    await db.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (DbUpdateException)
        {
            // Another call with the same instruction id, or another write on the world, got there first.
            db.ChangeTracker.Clear();
            var winner = await db.BankPayments.AsNoTracking()
                .SingleOrDefaultAsync(row => row.BankAccountId == account.Id && row.InstructionId == instructionId, cancellationToken);
            return winner is null ? Results.StatusCode(StatusCodes.Status409Conflict) : Replay(winner, hash);
        }

        return Results.Ok(PaymentAnswer.From(payment));
    }

    private static IResult Replay(BankPayment known, byte[] hash) =>
        CryptographicOperations.FixedTimeEquals(known.RequestHash, hash)
            ? Results.Ok(PaymentAnswer.From(known))
            : Results.StatusCode(StatusCodes.Status409Conflict);

    /// <summary>R49: the field rules of a payment.</summary>
    private static bool Valid(PaymentRequest body, BankAccount account) =>
        body.InstructionId?.Length is >= 1 and <= BankRules.InstructionIdMaxLength
        && BankRules.IsAmount(body.Amount)
        && string.Equals(body.Currency, account.Currency.Trim(), StringComparison.Ordinal)
        && (body.Reference?.Length ?? 0) <= BankRules.PaymentReferenceMaxLength
        && body.Payee is not null
        && BankRules.IsAccountNumber(body.Payee.AccountNumber)
        && BankRules.IsName(body.Payee.Name);

    private static byte[] Hash(PaymentRequest body) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object?[]
        {
            body.InstructionId,
            body.Amount!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            body.Currency,
            body.Reference,
            body.Payee!.AccountNumber,
            body.Payee.Name
        })));

    /// <summary>
    /// R43: a line takes the next place of its account when simulator now reaches its <c>availableAt</c>, two places
    /// with <c>deliverTwice</c>. The account row is locked so two reads at once place a line only once.
    /// </summary>
    private static Task PlaceAvailableLinesAsync(
        SimulatorDbContext db,
        int accountId,
        DateTimeOffset simulatorNow,
        CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await PlaceInTransactionAsync(db, accountId, simulatorNow, cancellationToken);
        });

    private static async Task PlaceInTransactionAsync(
        SimulatorDbContext db,
        int accountId,
        DateTimeOffset simulatorNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM [BankAccounts] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {accountId}", cancellationToken);
        var unplaced = await db.BankLines
            .Where(line => line.BankAccountId == accountId
                           && line.AvailableAt <= simulatorNow
                           && !db.BankFeedPlaces.Any(place => place.BankLineId == line.Id))
            .OrderBy(line => line.AvailableAt)
            .ThenBy(line => line.Id)
            .ToListAsync(cancellationToken);
        if (unplaced.Count == 0)
        {
            return;
        }

        foreach (var line in unplaced)
        {
            // One save per place keeps the identity order equal to the feed order.
            db.BankFeedPlaces.Add(BankFeedPlace.For(line));
            await db.SaveChangesAsync(cancellationToken);
            if (line.DeliverTwice)
            {
                db.BankFeedPlaces.Add(BankFeedPlace.For(line));
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed record PaymentRequest(
    string? InstructionId,
    decimal? Amount,
    string? Currency,
    string? Reference,
    PaymentPayee? Payee);

public sealed record PaymentPayee(string? AccountNumber, string? Name);

/// <summary>Withdrawals R50: the bank's answer to a payment.</summary>
public sealed record PaymentAnswer(string PaymentId, string Status, string? Reason)
{
    public static PaymentAnswer From(BankPayment payment) => payment.Status == BankPaymentStatus.Rejected
        ? new PaymentAnswer(payment.PaymentId, "Rejected", payment.RejectReason)
        : new PaymentAnswer(payment.PaymentId, "Accepted", null);
}

/// <summary>R45: the wire item; the payer account is sent in full. On a debit line the payer fields carry the payee;
/// <c>endToEndId</c>, <c>returnOf</c> and <c>returnReason</c> come with withdrawals R48.</summary>
public sealed record BankFeedItem(
    string Id,
    DateOnly ValueDate,
    string Direction,
    decimal Amount,
    string Currency,
    string? PayerName,
    string? PayerAccount,
    string? Reference,
    string? EndToEndId,
    string? ReturnOf,
    string? ReturnReason);

public sealed record BankFeedResponse(IReadOnlyList<BankFeedItem> Items, string Cursor, bool HasMore);
