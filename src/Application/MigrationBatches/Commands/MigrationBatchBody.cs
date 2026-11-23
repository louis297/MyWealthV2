using System.Text.Json;

namespace MyWealthV2.Application.MigrationBatches.Commands;

/// <summary>Migration batches R3–R5, §8.1: the body of create and replace.</summary>
public abstract class MigrationBatchBody
{
    public string? Name { get; init; }

    public string? PreviousProvider { get; init; }

    public DateOnly? AsOfDate { get; init; }

    public MigrationControlTotalsBody? ControlTotals { get; init; }

    public List<MigrationEntryBody>? Entries { get; init; }

    public JsonElement? TenantId { get; init; }
}

public sealed class MigrationControlTotalsBody
{
    public int? AccountCount { get; init; }

    public List<MigrationMoneyBody>? Cash { get; init; }

    public List<MigrationQuantityBody>? Quantity { get; init; }

    public List<MigrationMoneyBody>? Cost { get; init; }
}

public sealed class MigrationMoneyBody
{
    public string? Currency { get; init; }

    public decimal? Amount { get; init; }
}

public sealed class MigrationQuantityBody
{
    public Guid? InstrumentId { get; init; }

    public decimal? Quantity { get; init; }
}

public sealed class MigrationEntryBody
{
    public Guid? AccountId { get; init; }

    public string? ExternalReference { get; init; }

    public decimal? Cash { get; init; }

    public List<MigrationHoldingBody>? Holdings { get; init; }
}

public sealed class MigrationHoldingBody
{
    public Guid? InstrumentId { get; init; }

    public decimal? Quantity { get; init; }

    public decimal? Cost { get; init; }
}

/// <summary>Migration batches R3–R5: the shape of a create or replace body, with each error at its place.</summary>
internal static class MigrationBatchBodyRules
{
    public const string NotFoundMessage = "Not found in this tenant.";

    public static bool IsAbsent(JsonElement? value) => value is null || value.Value.ValueKind == JsonValueKind.Null;

    public static void Apply<T>(AbstractValidator<T> validator, TimeProvider time)
        where T : MigrationBatchBody
    {
        var max = Domain.Entities.MigrationBatch.TextMaxLength;
        validator.RuleFor(body => body.Name).Must(IsText).WithMessage($"Name must be 1 to {max} characters.");
        validator.RuleFor(body => body.PreviousProvider).Must(IsText).WithMessage($"Previous provider must be 1 to {max} characters.");
        validator.RuleFor(body => body.AsOfDate).NotNull().WithMessage("As of date is required.");
        validator.RuleFor(body => body.AsOfDate)
            .Must(date => date is null || date.Value <= DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime))
            .WithMessage("As of date cannot be after today.");
        validator.RuleFor(body => body.ControlTotals).NotNull().WithMessage("Control totals are required.");
        validator.RuleFor(body => body.ControlTotals!).SetValidator(new ControlTotalsValidator()).When(body => body.ControlTotals is not null);
        validator.RuleFor(body => body.Entries).Custom((entries, context) =>
        {
            if (entries is null)
            {
                return;
            }

            if (entries.Count > Domain.Entities.MigrationBatch.MaxEntries)
            {
                context.AddFailure("Entries", $"A migration batch has at most {Domain.Entities.MigrationBatch.MaxEntries} entries.");
            }

            if (entries.Sum(entry => entry?.Holdings?.Count ?? 0) > Domain.Entities.MigrationBatch.MaxHoldings)
            {
                context.AddFailure("Entries", $"A migration batch has at most {Domain.Entities.MigrationBatch.MaxHoldings} holdings.");
            }

            var seen = new HashSet<Guid>();
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index]?.AccountId is Guid accountId && accountId != Guid.Empty && !seen.Add(accountId))
                {
                    context.AddFailure($"Entries[{index}].AccountId", "This account is already in the batch.");
                }
            }
        });
        validator.RuleForEach(body => body.Entries).NotNull().SetValidator(new EntryValidator());
        validator.RuleFor(body => body.TenantId).Must(IsAbsent).WithMessage("TenantId cannot be set.");
    }

    private static bool IsText(string? value) =>
        value?.Trim().Length is > 0 and <= Domain.Entities.MigrationBatch.TextMaxLength;

    private static bool Fits(decimal value, int places) => decimal.Round(value, places) == value;

    private sealed class ControlTotalsValidator : AbstractValidator<MigrationControlTotalsBody>
    {
        public ControlTotalsValidator()
        {
            RuleFor(totals => totals.AccountCount).NotNull().WithMessage("Account count is required.")
                .GreaterThanOrEqualTo(0).WithMessage("Account count must be 0 or more.");
            RuleForEach(totals => totals.Cash).NotNull().SetValidator(new MoneyValidator());
            RuleForEach(totals => totals.Cost).NotNull().SetValidator(new MoneyValidator());
            RuleForEach(totals => totals.Quantity).NotNull().SetValidator(new QuantityValidator());
            RuleFor(totals => totals.Cash).Must(Distinct).WithMessage("Each currency appears at most once.");
            RuleFor(totals => totals.Cost).Must(Distinct).WithMessage("Each currency appears at most once.");
            RuleFor(totals => totals.Quantity)
                .Must(rows => rows is null || rows.Where(row => row?.InstrumentId is not null)
                    .GroupBy(row => row!.InstrumentId).All(group => group.Count() == 1))
                .WithMessage("Each instrument appears at most once.");
        }

        private static bool Distinct(List<MigrationMoneyBody>? rows) =>
            rows is null || rows.Where(row => row?.Currency is not null)
                .GroupBy(row => row!.Currency!.Trim().ToUpperInvariant()).All(group => group.Count() == 1);
    }

    private sealed class MoneyValidator : AbstractValidator<MigrationMoneyBody>
    {
        public MoneyValidator()
        {
            RuleFor(row => row.Currency).Must(currency => currency?.Trim().Length == 3)
                .WithMessage("Currency must be a 3-letter code.");
            RuleFor(row => row.Amount).NotNull().WithMessage("Amount is required.")
                .GreaterThanOrEqualTo(0).WithMessage("Amount must be 0 or more.");
        }
    }

    private sealed class QuantityValidator : AbstractValidator<MigrationQuantityBody>
    {
        public QuantityValidator()
        {
            RuleFor(row => row.InstrumentId).NotEmpty().WithMessage("Instrument is required.");
            RuleFor(row => row.Quantity).NotNull().WithMessage("Quantity is required.")
                .GreaterThanOrEqualTo(0).WithMessage("Quantity must be 0 or more.");
        }
    }

    private sealed class EntryValidator : AbstractValidator<MigrationEntryBody>
    {
        public EntryValidator()
        {
            RuleFor(entry => entry.AccountId).NotEmpty().WithMessage("Account is required.");
            RuleFor(entry => entry.ExternalReference)
                .Must(reference => reference is null || reference.Trim().Length <= Domain.Entities.MigrationBatch.TextMaxLength)
                .WithMessage($"External reference must be at most {Domain.Entities.MigrationBatch.TextMaxLength} characters.");
            RuleFor(entry => entry.Cash)
                .Must((entry, cash) => cash is not null || entry.Holdings is { Count: > 0 })
                .WithMessage("An entry has cash, at least one holding, or both.");
            RuleFor(entry => entry.Cash)
                .Must(cash => cash is null || (cash > 0 && Fits(cash.Value, Domain.Entities.MigrationBatch.CashDecimals)))
                .WithMessage($"Cash must be greater than 0 with at most {Domain.Entities.MigrationBatch.CashDecimals} decimal places.");
            RuleForEach(entry => entry.Holdings).NotNull().SetValidator(new HoldingValidator());
            RuleFor(entry => entry.Holdings).Custom((holdings, context) =>
            {
                if (holdings is null)
                {
                    return;
                }

                var seen = new HashSet<Guid>();
                for (var index = 0; index < holdings.Count; index++)
                {
                    if (holdings[index]?.InstrumentId is Guid instrumentId && instrumentId != Guid.Empty && !seen.Add(instrumentId))
                    {
                        context.AddFailure($"{context.PropertyPath}[{index}].InstrumentId", "This instrument is already in the entry.");
                    }
                }
            });
        }
    }

    private sealed class HoldingValidator : AbstractValidator<MigrationHoldingBody>
    {
        public HoldingValidator()
        {
            RuleFor(holding => holding.InstrumentId).NotEmpty().WithMessage("Instrument is required.");
            RuleFor(holding => holding.Quantity).NotNull().WithMessage("Quantity is required.")
                .Must(quantity => quantity is null || (quantity > 0 && Fits(quantity.Value, Domain.Entities.MigrationBatch.QuantityDecimals)))
                .WithMessage($"Quantity must be greater than 0 with at most {Domain.Entities.MigrationBatch.QuantityDecimals} decimal places.");
            RuleFor(holding => holding.Cost).NotNull().WithMessage("Cost is required.")
                .Must(cost => cost is null || (cost >= 0 && Fits(cost.Value, Domain.Entities.MigrationBatch.CostDecimals)))
                .WithMessage($"Cost must be 0 or more with at most {Domain.Entities.MigrationBatch.CostDecimals} decimal places.");
        }
    }
}
