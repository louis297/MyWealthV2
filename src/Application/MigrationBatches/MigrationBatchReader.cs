using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Migrations;

namespace MyWealthV2.Application.MigrationBatches;

/// <summary>Migration batches R28, R29: the list item and the item of a batch.</summary>
internal static class MigrationBatchReader
{
    public static async Task<IReadOnlyList<MigrationBatchDto>> ToDtosAsync(
        IApplicationDbContext db,
        IReadOnlyList<MigrationBatch> rows,
        CancellationToken cancellationToken)
    {
        var batchIds = rows.Select(row => row.Id).ToList();
        var counts = await db.MigrationBatches.AsNoTracking()
            .Where(batch => batchIds.Contains(batch.Id))
            .Select(batch => new { batch.Id, Entries = batch.Entries.Count, Holdings = batch.Entries.Sum(entry => entry.Holdings.Count) })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var people = await PeopleAsync(db, rows, cancellationToken);
        return rows.Select(row => new MigrationBatchDto
        {
            Id = row.PublicId,
            Name = row.Name,
            PreviousProvider = row.PreviousProvider,
            AsOfDate = Date(row.AsOfDate),
            Status = row.Status.ToString(),
            EntryCount = counts[row.Id].Entries,
            HoldingCount = counts[row.Id].Holdings,
            CreatedBy = people.Creator(row),
            CreatedAt = row.Created,
            SubmittedBy = people.Person(row.SubmittedByUserId),
            SubmittedAt = row.SubmittedAt,
            PostedBy = people.Person(row.PostedByUserId),
            PostedAt = row.PostedAt
        }).ToList();
    }

    public static async Task<MigrationBatchItemDto> ToItemAsync(
        IApplicationDbContext db,
        ICurrencyCatalog catalog,
        MigrationBatch batch,
        CancellationToken cancellationToken)
    {
        var state = await MigrationBatchValidator.ValidateAsync(db, catalog, batch, cancellationToken);
        var people = await PeopleAsync(db, [batch], cancellationToken);
        var transactionIds = batch.Entries
            .SelectMany(entry => new[] { entry.OpeningTransactionId, entry.OpeningHoldingsTransactionId })
            .OfType<int>()
            .ToList();
        var transactions = await db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Where(transaction => transactionIds.Contains(transaction.Id))
            .ToDictionaryAsync(transaction => transaction.Id, transaction => transaction.PublicId, cancellationToken);
        Guid AccountId(int id) => state.Accounts[id].PublicId;
        Guid InstrumentId(int id) => state.Instruments[id].PublicId;
        var open = batch.Status is MigrationBatchStatus.Draft or MigrationBatchStatus.Submitted;
        return new MigrationBatchItemDto
        {
            Id = batch.PublicId,
            Name = batch.Name,
            PreviousProvider = batch.PreviousProvider,
            AsOfDate = Date(batch.AsOfDate),
            Status = batch.Status.ToString(),
            EntryCount = batch.Entries.Count,
            HoldingCount = batch.Entries.Sum(entry => entry.Holdings.Count),
            CreatedBy = people.Creator(batch),
            CreatedAt = batch.Created,
            SubmittedBy = people.Person(batch.SubmittedByUserId),
            SubmittedAt = batch.SubmittedAt,
            PostedBy = people.Person(batch.PostedByUserId),
            PostedAt = batch.PostedAt,
            ControlTotals = Totals(batch.ControlTotals, InstrumentId),
            ComputedTotals = Totals(state.Result.ComputedTotals, InstrumentId),
            FundingTotals = state.Result.FundingTotals.Select(total => new MigrationMoneyDto(total.Currency, total.Amount)).ToList(),
            Funding = state.Funding.Select(line => new MigrationFundingLineDto(line.PublicId, line.Currency, line.Amount,
                Date(line.ValueDate), line.PayerName, line.Reference)).ToList(),
            Entries = batch.Entries.OrderBy(entry => entry.Position).Select(entry =>
            {
                var account = state.Accounts[entry.AccountId];
                return new MigrationEntryDto(
                    new MigrationEntryAccountDto(account.PublicId, account.Name, account.DepositReference, account.Currency,
                        new PersonRefDto(account.CustomerPublicId, account.CustomerName)),
                    entry.ExternalReference,
                    entry.Cash,
                    entry.Holdings.Select(holding =>
                    {
                        var instrument = state.Instruments[holding.InstrumentId];
                        return new MigrationHoldingDto(
                            new MigrationInstrumentDto(instrument.PublicId, instrument.Symbol, instrument.Name, instrument.QuoteCurrency.Trim()),
                            holding.Quantity,
                            holding.Cost);
                    }).ToList(),
                    entry.OpeningTransactionId is int opening ? transactions[opening] : null,
                    entry.OpeningHoldingsTransactionId is int holdings ? transactions[holdings] : null);
            }).ToList(),
            Validation = open
                ? new MigrationValidationDto(state.Result.IsValid, state.Result.Issues.Select(issue => new MigrationIssueDto(
                    issue.Code.ToString(),
                    issue.AccountId is int account ? AccountId(account) : null,
                    issue.InstrumentId is int instrument ? InstrumentId(instrument) : null,
                    issue.Currency,
                    issue.Total?.ToString(),
                    issue.Message)).ToList())
                : null,
            Returned = batch.ReturnedAt is { } returnedAt
                ? new MigrationActionDto(people.Person(batch.ReturnedByUserId), returnedAt, batch.ReturnNote!)
                : null,
            Cancelled = batch.CancelledAt is { } cancelledAt
                ? new MigrationActionDto(people.Person(batch.CancelledByUserId), cancelledAt, batch.CancelNote!)
                : null,
            RowVersion = Convert.ToBase64String(batch.RowVersion ?? [])
        };
    }

    private static MigrationTotalsDto Totals(MigrationControlTotals totals, Func<int, Guid> instrumentId) =>
        new(
            totals.AccountCount,
            totals.Cash.Select(row => new MigrationMoneyDto(row.Currency, row.Amount)).ToList(),
            totals.Quantity.Select(row => new MigrationQuantityDto(instrumentId(row.InstrumentId), row.Quantity)).ToList(),
            totals.Cost.Select(row => new MigrationMoneyDto(row.Currency, row.Amount)).ToList());

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd");

    private static async Task<People> PeopleAsync(
        IApplicationDbContext db,
        IReadOnlyList<MigrationBatch> rows,
        CancellationToken cancellationToken)
    {
        var userIds = rows
            .SelectMany(row => new[] { row.SubmittedByUserId, row.ReturnedByUserId, row.PostedByUserId, row.CancelledByUserId })
            .OfType<int>()
            .Distinct()
            .ToList();
        var users = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => new PersonRefDto(user.PublicId, user.Name), cancellationToken);
        // CreatedBy holds the person's identity user id.
        var creators = rows.Select(row => row.CreatedBy).OfType<string>().Distinct().ToList();
        var tenantIds = rows.Select(row => row.TenantId).Distinct().ToList();
        var creatorRefs = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(user => user.TenantId != null && tenantIds.Contains(user.TenantId.Value) && creators.Contains(user.IdentityUserId))
            .ToDictionaryAsync(user => user.IdentityUserId, user => new PersonRefDto(user.PublicId, user.Name), cancellationToken);
        return new People(users, creatorRefs);
    }

    private sealed record People(
        IReadOnlyDictionary<int, PersonRefDto> Users,
        IReadOnlyDictionary<string, PersonRefDto> Creators)
    {
        public PersonRefDto? Person(int? id) => id is int value ? Users.GetValueOrDefault(value) : null;

        public PersonRefDto? Creator(MigrationBatch batch) =>
            batch.CreatedBy is not null ? Creators.GetValueOrDefault(batch.CreatedBy) : null;
    }
}
