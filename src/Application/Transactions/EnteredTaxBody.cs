using FluentValidation.Results;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.Transactions;

/// <summary>External income R5: one tax an outside payer withheld, as the body states it.</summary>
public sealed class EnteredTaxBody
{
    public string? Kind { get; init; }

    public decimal? RatePercent { get; init; }

    public decimal? Amount { get; init; }

    /// <summary>The kind by its exact name, or null.</summary>
    internal TaxKind? ParsedKind => Kind switch
    {
        nameof(TaxKind.Rwt) => TaxKind.Rwt,
        nameof(TaxKind.Nrwt) => TaxKind.Nrwt,
        nameof(TaxKind.ForeignWithholding) => TaxKind.ForeignWithholding,
        _ => null
    };

    /// <summary>The rows as entered taxes, or null while a row misses a field or names no kind.</summary>
    internal static IReadOnlyList<EnteredTax>? Parse(IReadOnlyList<EnteredTaxBody>? rows)
    {
        if (rows is null)
        {
            return [];
        }

        var taxes = new List<EnteredTax>(rows.Count);
        foreach (var row in rows)
        {
            if (row.ParsedKind is not { } kind || row.RatePercent is not { } rate || row.Amount is not { } amount)
            {
                return null;
            }

            taxes.Add(new EnteredTax(kind, rate, amount));
        }

        return taxes;
    }
}

/// <summary>External income R5–R7: the request-shape rules of <c>taxes</c>, shared by the dividend and the interest
/// create commands.</summary>
internal static class EnteredTaxRules
{
    public static void Add<T>(AbstractValidator<T> validator, Func<T, List<EnteredTaxBody>?> taxes)
    {
        validator.RuleFor(command => taxes(command)).Custom((rows, context) =>
        {
            if (rows is null || rows.Count == 0)
            {
                return;
            }

            if (rows.Count > EnteredTaxes.MaxCount)
            {
                context.AddFailure("Taxes", EnteredTaxes.TooManyMessage);
                return;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row is null || row.ParsedKind is null)
                {
                    context.AddFailure($"Taxes[{i}].Kind", EnteredTaxes.KindMessage);
                }

                if (row?.RatePercent is not { } rate || rate is <= 0m or > 100m
                    || !FxConversionPricing.FitsDecimalPlaces(rate, EnteredTaxes.RateDecimals))
                {
                    context.AddFailure($"Taxes[{i}].RatePercent", EnteredTaxes.RateMessage);
                }

                if (row?.Amount is not { } amount || amount <= 0m)
                {
                    context.AddFailure($"Taxes[{i}].Amount", EnteredTaxes.AmountMessage);
                }
            }

            if (EnteredTaxBody.Parse(rows) is { } parsed && EnteredTaxes.RefusalOf(parsed) is { } refusal)
            {
                context.AddFailure(refusal.Field, refusal.Message);
            }
        });
    }

    /// <summary>R13, R22: each tax amount fits the currency's decimal places.</summary>
    public static List<ValidationFailure> DecimalPlaceFailures(IReadOnlyList<EnteredTax> taxes, int places)
    {
        var failures = new List<ValidationFailure>();
        for (var i = 0; i < taxes.Count; i++)
        {
            if (!FxConversionPricing.FitsDecimalPlaces(taxes[i].Amount, places))
            {
                failures.Add(new ValidationFailure($"Taxes[{i}].Amount", $"Amount can have at most {places} decimal places."));
            }
        }

        return failures;
    }
}
