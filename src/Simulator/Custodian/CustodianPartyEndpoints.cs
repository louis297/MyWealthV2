using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Bank;
using MyWealthV2.Simulator.Broker;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;

namespace MyWealthV2.Simulator.Custodian;

/// <summary>In-kind-transfers R62, R63, R69: the custodian's transfer route and feed on the party surface.</summary>
public static class CustodianPartyEndpoints
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 100;

    public static RouteGroupBuilder MapCustodianPartyEndpoints(this RouteGroupBuilder party)
    {
        party.MapPost("/custodian/accounts/{accountNumber}/transfers", TransferAsync);
        party.MapGet("/custodian/accounts/{accountNumber}/movements", GetMovementsAsync);
        return party;
    }

    /// <summary>R62–R65: one transfer instruction. Unknown account → 404; a body outside the rules → 400; the same
    /// instruction id and body again → the first answer; the same id with another body → 409.</summary>
    private static async Task<IResult> TransferAsync(
        string accountNumber,
        TransferRequest body,
        SimulatorDbContext db,
        TimeProvider time,
        BrokerBankAccounts brokerAccounts,
        CancellationToken cancellationToken)
    {
        var world = await BrokerBook.UpToDateAsync(db, time, brokerAccounts, cancellationToken);
        var account = await db.CustodianAccounts.AsNoTracking().SingleOrDefaultAsync(row => row.AccountNumber == accountNumber, cancellationToken);
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
        var known = await db.CustodianTransfers.AsNoTracking()
            .SingleOrDefaultAsync(row => row.AccountNumber == account.AccountNumber && row.InstructionId == instructionId, cancellationToken);
        if (known is not null)
        {
            return Replay(known, hash, world.WorldId);
        }

        var simulatorNow = world.Now(time.GetUtcNow());
        try
        {
            CustodianTransfer? transfer = null;
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await CustodianBook.LockAsync(db, cancellationToken);
                var settings = await db.CustodianSettings.AsNoTracking().SingleAsync(cancellationToken);
                var number = (await db.CustodianTransfers.MaxAsync(row => (int?)row.Number, cancellationToken) ?? 0) + 1;

                // R63: a simulated instrument moves in its own currency only, whatever the transfer mode.
                var instrument = await db.MarketInstruments.AsNoTracking().SingleOrDefaultAsync(row => row.Symbol == body.Symbol, cancellationToken);
                var reason = instrument is not null && instrument.Currency.Trim() != body.Currency
                    ? "UnknownInstrument"
                    : settings.TransferMode == CustodianTransferMode.Reject ? settings.RejectReason : null;
                var status = reason is not null
                    ? CustodianTransferStatus.Rejected
                    : settings.TransferMode == CustodianTransferMode.Hold ? CustodianTransferStatus.Held : CustodianTransferStatus.Executed;
                transfer = CustodianTransfer.Receive(number, account.AccountNumber, instructionId, JsonSerializer.Serialize(body), hash,
                    status == CustodianTransferStatus.Executed ? CustodianTransferStatus.Held : status, settings.TransferMode, reason,
                    simulatorNow);
                db.CustodianTransfers.Add(transfer);
                await db.SaveChangesAsync(cancellationToken);
                if (status == CustodianTransferStatus.Executed)
                {
                    await ExecuteAsync(db, transfer, body, body.Quantity!.Value, simulatorNow, cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            });
            return Results.Ok(TransferAnswer.From(transfer!, world.WorldId));
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.CustodianTransfers.AsNoTracking()
                .SingleOrDefaultAsync(row => row.AccountNumber == account.AccountNumber && row.InstructionId == instructionId, cancellationToken);
            return winner is null ? Results.StatusCode(StatusCodes.Status409Conflict) : Replay(winner, hash, world.WorldId);
        }
    }

    /// <summary>R65: an executed transfer adds one movement of kind <c>Transfer</c> to the account, in the feed at once.</summary>
    internal static async Task ExecuteAsync(
        SimulatorDbContext db, CustodianTransfer transfer, TransferRequest body, decimal quantity, DateTimeOffset simulatorNow,
        CancellationToken cancellationToken)
    {
        var movement = await CustodianBook.AddAsync(db, transfer.AccountNumber, CustodianRules.Direction(body.Direction!),
            CustodianMovementKind.Transfer, body.Symbol!.Trim(), body.Currency!, quantity, MarketPricePath.Day(simulatorNow), simulatorNow,
            instructionId: transfer.InstructionId,
            counterpartyName: CustodianRules.Text(body.Counterparty?.Name, CustodianRules.FreeTextMaxLength),
            counterpartyAccount: CustodianRules.Text(body.Counterparty?.Account, CustodianRules.CounterpartyAccountMaxLength),
            cancellationToken: cancellationToken);
        transfer.Executed(movement.Number);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>R69: the account's movements after the cursor, oldest first, with the positions at the returned cursor.</summary>
    private static async Task<IResult> GetMovementsAsync(
        string accountNumber,
        string? after,
        int? limit,
        SimulatorDbContext db,
        TimeProvider time,
        BrokerBankAccounts brokerAccounts,
        CancellationToken cancellationToken)
    {
        var world = await BrokerBook.UpToDateAsync(db, time, brokerAccounts, cancellationToken);
        if (!BankFeedCursor.TryRead(after, world.WorldId, out var afterPlace))
        {
            return Results.BadRequest();
        }

        var account = await db.CustodianAccounts.AsNoTracking().SingleOrDefaultAsync(row => row.AccountNumber == accountNumber, cancellationToken);
        if (account is null)
        {
            return Results.NotFound();
        }

        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var rows = await db.CustodianMovements.AsNoTracking()
            .Where(row => row.AccountNumber == account.AccountNumber && row.Place > afterPlace)
            .OrderBy(row => row.Place)
            .ToListAsync(cancellationToken);

        // R69: a movement left out of the feed keeps its place and is passed over; the cursor passes trailing ones.
        var page = rows.Where(row => !row.OmitFromFeed).Take(take).ToList();
        var last = page.Count > 0 ? page[^1].Place : (int)afterPlace;
        var hasMore = rows.Any(row => row.Place > last && !row.OmitFromFeed);
        if (!hasMore && rows.Count > 0)
        {
            last = Math.Max(last, rows[^1].Place);
        }

        var positions = (await CustodianBook.PositionsAsync(db, account.AccountNumber, last, cancellationToken))
            .Select(position => new CustodianPositionItem(position.Symbol, account.Currency.Trim(), position.Quantity))
            .ToList();
        var items = page.Select(row => new CustodianMovementItem(
            CustodianMovement.MovementId(world.WorldId, row.Number),
            row.Direction == CustodianMovement.InDirection ? "In" : "Out",
            row.Kind.ToString(),
            row.Symbol,
            row.Currency.Trim(),
            row.Quantity,
            row.SettlementDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            row.InstructionId,
            row.TradeReference,
            row.CorporateActionId,
            row.CounterpartyName,
            row.CounterpartyAccount,
            row.Reference)).ToList();
        return Results.Ok(new CustodianFeedResponse(items, BankFeedCursor.Format(world.WorldId, last), hasMore, positions));
    }

    private static IResult Replay(CustodianTransfer known, byte[] hash, Guid worldId) =>
        CryptographicOperations.FixedTimeEquals(known.RequestHash, hash)
            ? Results.Ok(TransferAnswer.From(known, worldId))
            : Results.StatusCode(StatusCodes.Status409Conflict);

    /// <summary>R62: the field rules of a transfer.</summary>
    private static bool Valid(TransferRequest body, CustodianAccount account) =>
        body.InstructionId?.Length is >= 1 and <= CustodianRules.InstructionIdMaxLength
        && CustodianRules.IsDirection(body.Direction)
        && CustodianRules.IsSymbol(body.Symbol)
        && string.Equals(body.Currency, account.Currency.Trim(), StringComparison.Ordinal)
        && CustodianRules.IsQuantity(body.Quantity)
        && body.Counterparty?.Name?.Trim().Length is >= 1 and <= CustodianRules.CounterpartyNameMaxLength
        && CustodianRules.IsAccountNumber(body.Counterparty.Account)
        && body.Counterparty.HolderName?.Trim().Length is >= 1 and <= CustodianRules.HolderNameMaxLength;

    private static byte[] Hash(TransferRequest body) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object?[]
        {
            body.InstructionId,
            body.Direction,
            body.Symbol,
            body.Currency,
            body.Quantity!.Value.ToString("0.########", CultureInfo.InvariantCulture),
            body.Counterparty!.Name,
            body.Counterparty.Account,
            body.Counterparty.HolderName
        })));
}

public sealed record TransferRequest(
    string? InstructionId,
    string? Direction,
    string? Symbol,
    string? Currency,
    decimal? Quantity,
    TransferCounterparty? Counterparty);

public sealed record TransferCounterparty(string? Name, string? Account, string? HolderName);

/// <summary>R63: the custodian's answer to an instruction; a held transfer is accepted.</summary>
public sealed record TransferAnswer(
    string TransferId,
    string Status,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Reason)
{
    public static TransferAnswer From(CustodianTransfer transfer, Guid worldId) => transfer.Status == CustodianTransferStatus.Rejected
        ? new TransferAnswer(CustodianTransfer.TransferId(worldId, transfer.Number), "Rejected", transfer.RejectReason)
        : new TransferAnswer(CustodianTransfer.TransferId(worldId, transfer.Number), "Accepted", null);
}

/// <summary>R69: the wire item; a field a movement does not use is null.</summary>
public sealed record CustodianMovementItem(
    string Id,
    string Direction,
    string Kind,
    string Symbol,
    string Currency,
    decimal Quantity,
    string SettlementDate,
    string? InstructionId,
    string? TradeReference,
    string? CorporateActionId,
    string? CounterpartyName,
    string? CounterpartyAccount,
    string? Reference);

public sealed record CustodianPositionItem(string Symbol, string Currency, decimal Quantity);

public sealed record CustodianFeedResponse(
    IReadOnlyList<CustodianMovementItem> Items,
    string Cursor,
    bool HasMore,
    IReadOnlyList<CustodianPositionItem> Positions);
