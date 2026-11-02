namespace MyWealthV2.Domain.Entities;

public readonly record struct PortfolioSettings(
    decimal DriftAbsolute,
    decimal DriftRelative,
    decimal MinimumTrade,
    decimal CashResidualPercent)
{
    public static PortfolioSettings Default { get; } = new(1m, 5m, 100m, 1m);
}
