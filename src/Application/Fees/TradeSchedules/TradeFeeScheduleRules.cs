using FluentValidation.Results;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.Fees.TradeSchedules;

/// <summary>
/// The R5 shape checks with the request field names, shared by create, update and the fee preview (R31), so every
/// route reports the same <c>errors</c> keys. The Domain repeats them as a backstop.
/// </summary>
internal static class TradeFeeScheduleRules
{
    public static List<ValidationFailure> Check(
        decimal minimum,
        decimal? cap,
        IReadOnlyList<FeeBandInput>? bands,
        int decimalPlaces)
    {
        var failures = new List<ValidationFailure>();
        if (minimum < 0m || !FeeBands.HasAtMostDecimals(minimum, decimalPlaces))
        {
            failures.Add(new ValidationFailure("Minimum",
                $"Minimum must be at least 0 with at most {decimalPlaces} decimals."));
        }

        if (cap is { } maximum)
        {
            if (maximum <= 0m || !FeeBands.HasAtMostDecimals(maximum, decimalPlaces))
            {
                failures.Add(new ValidationFailure("Cap", $"Cap must be above 0 with at most {decimalPlaces} decimals."));
            }
            else if (maximum < minimum)
            {
                failures.Add(new ValidationFailure("Cap", "The cap must be at least the minimum."));
            }
        }

        failures.AddRange(CheckBands(bands, decimalPlaces));
        return failures;
    }

    /// <summary>The R5 band shape with the request field names; reused by interest rate schedules (interest-run R9).</summary>
    public static List<ValidationFailure> CheckBands(IReadOnlyList<FeeBandInput>? bands, int decimalPlaces)
    {
        var failures = new List<ValidationFailure>();
        if (bands is null || bands.Count is < 1 or > FeeBands.MaxBands)
        {
            failures.Add(new ValidationFailure("Bands", $"A schedule needs 1 to {FeeBands.MaxBands} bands."));
            return failures;
        }

        for (var index = 0; index < bands.Count; index++)
        {
            var band = bands[index];
            var from = $"Bands[{index}].From";
            if (index == 0 && band.From != 0m)
            {
                failures.Add(new ValidationFailure(from, "The first band must start at 0."));
            }
            else if (index > 0 && band.From <= bands[index - 1].From)
            {
                failures.Add(new ValidationFailure(from, "Each band must start above the one before it."));
            }
            else if (!FeeBands.HasAtMostDecimals(band.From, decimalPlaces))
            {
                failures.Add(new ValidationFailure(from, $"A band start can have at most {decimalPlaces} decimals."));
            }

            if (band.RatePercent is < 0m or > 100m || !FeeBands.HasAtMostDecimals(band.RatePercent, FeeBands.RateDecimalPlaces))
            {
                failures.Add(new ValidationFailure($"Bands[{index}].RatePercent",
                    $"The rate must be between 0 and 100 with at most {FeeBands.RateDecimalPlaces} decimals."));
            }
        }

        return failures;
    }

    public static IReadOnlyList<FeeBand> ToBands(IEnumerable<FeeBandInput> bands) =>
        bands.Select(band => new FeeBand(band.From, band.RatePercent)).ToList();
}
