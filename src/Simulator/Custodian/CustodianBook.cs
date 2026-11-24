using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;

namespace MyWealthV2.Simulator.Custodian;

/// <summary>The custodian's books: a movement takes the next number of the world and the next place of its account.</summary>
public static class CustodianBook
{
    /// <summary>The lock every write on the custodian's books holds until its transaction ends, so numbers and places
    /// follow the order the writes happen in.</summary>
    public static Task LockAsync(SimulatorDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync("SELECT 1 FROM [CustodianSettings] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = 1", cancellationToken);

    /// <summary>Adds one movement and saves it. The caller holds <see cref="LockAsync"/> in its transaction when several
    /// writers may race.</summary>
    public static async Task<CustodianMovement> AddAsync(
        SimulatorDbContext db,
        string accountNumber,
        int direction,
        CustodianMovementKind kind,
        string symbol,
        string currency,
        decimal quantity,
        DateOnly settlementDate,
        DateTimeOffset createdAt,
        string? instructionId = null,
        string? tradeReference = null,
        string? corporateActionId = null,
        string? counterpartyName = null,
        string? counterpartyAccount = null,
        string? reference = null,
        bool omitFromFeed = false,
        CancellationToken cancellationToken = default)
    {
        var number = (await db.CustodianMovements.MaxAsync(row => (int?)row.Number, cancellationToken) ?? 0) + 1;
        var place = (await db.CustodianMovements.Where(row => row.AccountNumber == accountNumber)
            .MaxAsync(row => (int?)row.Place, cancellationToken) ?? 0) + 1;
        var movement = CustodianMovement.Create(number, place, accountNumber, direction, kind, symbol, currency, quantity, settlementDate,
            createdAt, instructionId, tradeReference, corporateActionId, counterpartyName, counterpartyAccount, reference, omitFromFeed);
        db.CustodianMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);
        return movement;
    }

    /// <summary>R60, R69: the signed sum per symbol of an account's movements with a place up to <paramref name="place"/>
    /// (every place when null), for every symbol whose sum is not 0.</summary>
    public static async Task<List<(string Symbol, decimal Quantity)>> PositionsAsync(
        SimulatorDbContext db, string accountNumber, int? place, CancellationToken cancellationToken)
    {
        var rows = await db.CustodianMovements.AsNoTracking()
            .Where(row => row.AccountNumber == accountNumber && (place == null || row.Place <= place))
            .Select(row => new { row.Symbol, Signed = row.Direction == CustodianMovement.InDirection ? row.Quantity : -row.Quantity })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.First().Symbol, group.Sum(row => row.Signed)))
            .Where(position => position.Item2 != 0m)
            .OrderBy(position => position.Item1, StringComparer.Ordinal)
            .ToList();
    }
}
