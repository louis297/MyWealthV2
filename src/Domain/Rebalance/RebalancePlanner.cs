namespace MyWealthV2.Domain.Rebalance;

public static class RebalancePlanner
{
    public static RebalancePlan Plan(RebalanceInput input)
    {
        _ = input;
        return new RebalancePlan(RebalanceStatus.NoChange, null, null, []);
    }
}

public sealed record RebalanceInput(
    string BaseCurrency,
    string AccountCurrency,
    decimal Cash,
    decimal? AccountToBase,
    decimal DriftAbsolute,
    decimal DriftRelative,
    decimal MinimumTrade,
    decimal CashResidualPercent,
    IReadOnlyList<PlannerHolding> Holdings,
    IReadOnlyList<PlannerTarget> Targets);

public sealed record PlannerHolding(
    int InstrumentId,
    string Symbol,
    string QuoteCurrency,
    decimal Quantity,
    decimal? Price,
    decimal? QuoteToBase,
    decimal? QuoteToAccount);

public sealed record PlannerTarget(
    int InstrumentId,
    string Symbol,
    string QuoteCurrency,
    decimal TargetWeight,
    decimal? Price,
    decimal? QuoteToBase,
    decimal? QuoteToAccount);

public sealed record RebalanceTrade(
    int InstrumentId,
    string Symbol,
    bool Buy,
    decimal Quantity,
    decimal Price,
    string QuoteCurrency,
    decimal QuoteToAccount);

public sealed record RebalancePlan(
    RebalanceStatus Status,
    string? Code,
    string? Message,
    IReadOnlyList<RebalanceTrade> Trades);
