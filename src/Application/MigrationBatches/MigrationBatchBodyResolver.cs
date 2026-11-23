using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.MigrationBatches.Commands;
using MyWealthV2.Domain.Migrations;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.MigrationBatches;

/// <summary>Migration batches R7: turns the PublicIds of a body into the Tenant's Accounts and Instruments. An id that
/// names none of them is a 400 at its place in the body, whatever Tenant it belongs to.</summary>
internal static class MigrationBatchBodyResolver
{
    public static async Task<(MigrationBatchHeader Header, MigrationControlTotals Totals, List<MigrationEntrySpec> Entries)> ResolveAsync(
        IApplicationDbContext db,
        int tenantId,
        MigrationBatchBody body,
        CancellationToken cancellationToken)
    {
        var entries = body.Entries ?? [];
        var totals = body.ControlTotals!;
        var accountIds = entries.Select(entry => entry.AccountId!.Value).Distinct().ToList();
        var instrumentIds = entries.SelectMany(entry => entry.Holdings ?? []).Select(holding => holding.InstrumentId!.Value)
            .Concat((totals.Quantity ?? []).Select(row => row.InstrumentId!.Value))
            .Distinct()
            .ToList();
        var accounts = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
            .Where(account => account.TenantId == tenantId && accountIds.Contains(account.PublicId))
            .ToDictionaryAsync(account => account.PublicId, account => account.Id, cancellationToken);
        var instruments = await db.Instruments.IgnoreQueryFilters().AsNoTracking()
            .Where(instrument => instrument.TenantId == tenantId && instrumentIds.Contains(instrument.PublicId))
            .ToDictionaryAsync(instrument => instrument.PublicId, instrument => instrument.Id, cancellationToken);

        var failures = new List<ValidationFailure>();
        int Account(Guid id, string place) => Lookup(accounts, id, place, failures);
        int Instrument(Guid id, string place) => Lookup(instruments, id, place, failures);

        var specs = entries.Select((entry, index) => new MigrationEntrySpec(
            Account(entry.AccountId!.Value, $"Entries[{index}].AccountId"),
            entry.ExternalReference,
            entry.Cash,
            (entry.Holdings ?? []).Select((holding, position) => new MigrationHoldingSpec(
                Instrument(holding.InstrumentId!.Value, $"Entries[{index}].Holdings[{position}].InstrumentId"),
                holding.Quantity!.Value,
                holding.Cost!.Value)).ToList())).ToList();
        var quantity = (totals.Quantity ?? []).Select((row, index) => new MigrationInstrumentQuantity(
            Instrument(row.InstrumentId!.Value, $"ControlTotals.Quantity[{index}].InstrumentId"), row.Quantity!.Value)).ToList();
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var controlTotals = new MigrationControlTotals(
            totals.AccountCount!.Value,
            (totals.Cash ?? []).Select(row => new MigrationCurrencyAmount(row.Currency!.Trim().ToUpperInvariant(), row.Amount!.Value)).ToList(),
            quantity,
            (totals.Cost ?? []).Select(row => new MigrationCurrencyAmount(row.Currency!.Trim().ToUpperInvariant(), row.Amount!.Value)).ToList());
        return (new MigrationBatchHeader(body.Name!, body.PreviousProvider!, body.AsOfDate!.Value), controlTotals, specs);
    }

    private static int Lookup(Dictionary<Guid, int> ids, Guid id, string place, List<ValidationFailure> failures)
    {
        if (ids.TryGetValue(id, out var internalId))
        {
            return internalId;
        }

        failures.Add(new ValidationFailure(place, MigrationBatchBodyRules.NotFoundMessage));
        return 0;
    }
}
