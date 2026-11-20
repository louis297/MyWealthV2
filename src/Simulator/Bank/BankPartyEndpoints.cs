using System.Data;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;

namespace MyWealthV2.Simulator.Bank;

/// <summary>Deposit matching R43–R45: the statement feed the platform's bank feed adapter reads.</summary>
public static class BankPartyEndpoints
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 100;

    public static RouteGroupBuilder MapBankPartyEndpoints(this RouteGroupBuilder party)
    {
        party.MapGet("/bank/accounts/{accountNumber}/statement-lines", GetStatementLinesAsync);
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
                select new { place.Place, line })
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
            row.line.Reference)).ToList();

        // The place of the last item; with no item, the request's place in this world (0: before the first place).
        var cursor = BankFeedCursor.Format(world.WorldId, page.Count > 0 ? page[^1].Place : afterPlace);
        return Results.Ok(new BankFeedResponse(items, cursor, rows.Count > take));
    }

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

/// <summary>R45: the wire item. Every line in this slice is a credit; the payer account is sent in full.</summary>
public sealed record BankFeedItem(
    string Id,
    DateOnly ValueDate,
    string Direction,
    decimal Amount,
    string Currency,
    string? PayerName,
    string? PayerAccount,
    string? Reference);

public sealed record BankFeedResponse(IReadOnlyList<BankFeedItem> Items, string Cursor, bool HasMore);
