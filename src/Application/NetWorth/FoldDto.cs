using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.NetWorth;

/// <summary>The rate row a fold line used (valuation-history R12).</summary>
public sealed record FoldRateDto(decimal Rate, DateOnly Date, string Source);

/// <summary>One currency of an FX fold (valuation-history R12): <c>value</c> only when <c>status</c> is <c>Folded</c>.
/// <c>total</c> and <c>value</c> have the decimal places of their currency, the rate no trailing zeros (§8.1).</summary>
public sealed record FoldLineDto(string Currency, decimal Total, string Status, FoldRateDto? Fx, decimal? Value)
{
    public static FoldLineDto From(FoldLine line, Func<string, int> decimalPlaces, string reportingCurrency) => new(
        line.Currency,
        decimal.Round(line.Total, decimalPlaces(line.Currency), MidpointRounding.AwayFromZero),
        line.Status.ToString(),
        line.Fx is { } fx ? new FoldRateDto(fx.Rate / 1.000000000000000000000000000000000m, fx.AsOfDate, fx.Source) : null,
        line.Value is { } value ? decimal.Round(value, decimalPlaces(reportingCurrency), MidpointRounding.AwayFromZero) : null);
}
