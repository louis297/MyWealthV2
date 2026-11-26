using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.NetWorth;

/// <summary>The rate row a fold line used (valuation-history R12).</summary>
public sealed record FoldRateDto(decimal Rate, DateOnly Date, string Source);

/// <summary>One currency of an FX fold (valuation-history R12): <c>value</c> only when <c>status</c> is <c>Folded</c>.</summary>
public sealed record FoldLineDto(string Currency, decimal Total, string Status, FoldRateDto? Fx, decimal? Value)
{
    public static FoldLineDto From(FoldLine line) => new(
        line.Currency,
        line.Total,
        line.Status.ToString(),
        line.Fx is { } fx ? new FoldRateDto(fx.Rate, fx.AsOfDate, fx.Source) : null,
        line.Value);
}
