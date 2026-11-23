using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Migrations;

namespace MyWealthV2.Application.MigrationBatches;

/// <summary>An entry's Account as the batch item and the issues read it (R10, R29).</summary>
internal sealed record MigrationAccountRow(
    int Id,
    Guid PublicId,
    string Name,
    AccountType Type,
    CustodyMode CustodyMode,
    AccountStatus Status,
    string Currency,
    string? DepositReference,
    int CustomerId,
    Guid CustomerPublicId,
    string CustomerName,
    UserStatus CustomerStatus,
    bool HasTransactions);

/// <summary>A holding's or a control total's Instrument (R9, R29).</summary>
internal sealed record MigrationInstrumentRow(int Id, Guid PublicId, string Symbol, string Name, bool IsActive, string QuoteCurrency);

/// <summary>A statement line that funds the batch (R12).</summary>
internal sealed record MigrationFundingRow(Guid PublicId, string Currency, decimal Amount, DateOnly ValueDate, string? PayerName, string? Reference);

/// <summary>The state a batch was validated against, and the result (R8–R11).</summary>
internal sealed record MigrationBatchState(
    IReadOnlyDictionary<int, MigrationAccountRow> Accounts,
    IReadOnlyDictionary<int, MigrationInstrumentRow> Instruments,
    IReadOnlyList<MigrationFundingRow> Funding,
    MigrationValidationResult Result);

/// <summary>
/// Migration batches R8–R11: loads the current state of a batch's Accounts, Instruments and funding lines and calls
/// <see cref="MigrationBatchValidation.Compute"/>. The read, the submit and the post use it; the post calls it with
/// the Accounts locked.
/// </summary>
internal static class MigrationBatchValidator
{
    public static async Task<MigrationBatchState> ValidateAsync(
        IApplicationDbContext db,
        ICurrencyCatalog catalog,
        MigrationBatch batch,
        CancellationToken cancellationToken)
    {
        var entries = batch.Entries.OrderBy(entry => entry.Position).ToList();
        var accountIds = entries.Select(entry => entry.AccountId).ToList();
        var instrumentIds = entries.SelectMany(entry => entry.Holdings).Select(holding => holding.InstrumentId)
            .Concat(batch.ControlTotalRows.Where(row => row.InstrumentId is not null).Select(row => row.InstrumentId!.Value))
            .Distinct()
            .ToList();

        var withCash = await db.TransactionCashLegs.IgnoreQueryFilters().AsNoTracking()
            .Where(leg => accountIds.Contains(leg.AccountId)).Select(leg => leg.AccountId).Distinct().ToListAsync(cancellationToken);
        var withSecurities = await db.TransactionSecurityLegs.IgnoreQueryFilters().AsNoTracking()
            .Where(leg => accountIds.Contains(leg.AccountId)).Select(leg => leg.AccountId).Distinct().ToListAsync(cancellationToken);
        var used = withCash.Concat(withSecurities).ToHashSet();
        var accounts = (await (
                from account in db.Accounts.IgnoreQueryFilters().AsNoTracking()
                join customer in db.Users.IgnoreQueryFilters().AsNoTracking() on account.CustomerId equals customer.Id
                where accountIds.Contains(account.Id)
                select new { account.Id, account.PublicId, account.Name, account.Type, account.CustodyMode, account.Status, account.Currency,
                    account.DepositReference, account.CustomerId, CustomerPublicId = customer.PublicId, CustomerName = customer.Name,
                    CustomerStatus = customer.Status })
            .ToListAsync(cancellationToken))
            .ToDictionary(row => row.Id, row => new MigrationAccountRow(row.Id, row.PublicId, row.Name, row.Type, row.CustodyMode, row.Status,
                row.Currency.Trim(), row.DepositReference?.Value, row.CustomerId, row.CustomerPublicId, row.CustomerName,
                row.CustomerStatus, used.Contains(row.Id)));
        var instruments = await db.Instruments.IgnoreQueryFilters().AsNoTracking()
            .Where(instrument => instrumentIds.Contains(instrument.Id))
            .Select(instrument => new MigrationInstrumentRow(instrument.Id, instrument.PublicId, instrument.Symbol, instrument.Name,
                instrument.IsActive, instrument.QuoteCurrency))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var funding = await FundingAsync(db, batch, cancellationToken);

        var inputs = new MigrationValidationInputs(
            batch.ControlTotals,
            entries.Select(entry => new MigrationEntrySpec(entry.AccountId, entry.ExternalReference, entry.Cash,
                entry.Holdings.Select(holding => new MigrationHoldingSpec(holding.InstrumentId, holding.Quantity, holding.Cost)).ToList()))
                .ToList(),
            accounts.ToDictionary(pair => pair.Key, pair => new MigrationAccountState(pair.Value.Id, pair.Value.Name,
                pair.Value.CustodyMode, pair.Value.Status, pair.Value.CustomerStatus == UserStatus.Disabled, pair.Value.HasTransactions,
                pair.Value.Currency, catalog.TryGet(pair.Value.Currency)?.DecimalPlaces ?? 2)),
            instruments.ToDictionary(pair => pair.Key, pair => new MigrationInstrumentState(pair.Value.Id, pair.Value.Symbol,
                pair.Value.IsActive, pair.Value.QuoteCurrency.Trim())),
            funding.Select(line => new MigrationCurrencyAmount(line.Currency, line.Amount)).ToList());
        return new MigrationBatchState(accounts, instruments, funding, MigrationBatchValidation.Compute(inputs));
    }

    /// <summary>R12: the statement lines that fund the batch, oldest first.</summary>
    private static Task<List<MigrationFundingRow>> FundingAsync(
        IApplicationDbContext db,
        MigrationBatch batch,
        CancellationToken cancellationToken) =>
        db.BankStatementLines.IgnoreQueryFilters().AsNoTracking()
            .Where(line => line.MigrationBatchId == batch.Id && line.Status == BankStatementLineStatus.Matched)
            .OrderBy(line => line.Id)
            .Select(line => new MigrationFundingRow(line.PublicId, line.Currency, line.Amount, line.ValueDate, line.PayerName,
                line.ReferenceText))
            .ToListAsync(cancellationToken);
}
