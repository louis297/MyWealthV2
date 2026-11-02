namespace MyWealthV2.Domain.Rebalance;

public static class RebalancePlanner
{
    public static RebalancePlan Plan(RebalanceInput input)
    {
        var holdings = input.Holdings.Where(holding => holding.Quantity > 0m).ToList();
        var valued = holdings.Select(holding => Value(holding, input)).ToList();
        var targets = input.Targets.Select(target => Value(target, input)).ToList();
        var cashBase = input.Cash * Rate(input.AccountCurrency, input.BaseCurrency, input.AccountToBase);
        var total = cashBase + valued.Sum(row => row.MarketValueBase);
        var investable = total * (1m - input.CashResidualPercent / 100m);
        var trades = new List<RebalanceTrade>();
        var targetIds = targets.Select(target => target.InstrumentId).ToHashSet();

        foreach (var holding in valued.Where(row => !targetIds.Contains(row.InstrumentId)))
        {
            trades.Add(Sell(holding, holding.Quantity));
        }

        foreach (var target in targets)
        {
            var current = valued.SingleOrDefault(row => row.InstrumentId == target.InstrumentId);
            var currentValue = current?.MarketValueBase ?? 0m;
            var targetValue = decimal.Round(target.Weight / 100m * investable, 4, MidpointRounding.AwayFromZero);
            var delta = targetValue - currentValue;
            if (delta == 0m || SkipForDrift(input, total, currentValue, targetValue) || Math.Abs(delta) < input.MinimumTrade)
            {
                continue;
            }

            var unit = target.Price * target.ToBase;
            var quantity = decimal.Round(Math.Abs(delta) / unit, 8, MidpointRounding.AwayFromZero);
            if (quantity == 0m)
            {
                continue;
            }

            if (delta < 0m)
            {
                var held = current?.Quantity ?? 0m;
                if (held <= 0m)
                {
                    continue;
                }

                trades.Add(Sell(target, Math.Min(quantity, held)));
            }
            else
            {
                trades.Add(Buy(target, quantity));
            }
        }

        return trades.Count == 0
            ? new RebalancePlan(RebalanceStatus.NoChange, null, null, [])
            : new RebalancePlan(RebalanceStatus.Success, null, null, trades);
    }

    private static Valued Value(PlannerHolding holding, RebalanceInput input) =>
        Value(holding.InstrumentId, holding.Symbol, holding.QuoteCurrency, holding.Quantity, holding.Price!.Value,
            holding.QuoteToBase, holding.QuoteToAccount, input, 0m);

    private static Valued Value(PlannerTarget target, RebalanceInput input) =>
        Value(target.InstrumentId, target.Symbol, target.QuoteCurrency, 0m, target.Price!.Value,
            target.QuoteToBase, target.QuoteToAccount, input, target.TargetWeight);

    private static Valued Value(
        int instrumentId,
        string symbol,
        string quote,
        decimal quantity,
        decimal price,
        decimal? quoteToBase,
        decimal? quoteToAccount,
        RebalanceInput input,
        decimal weight)
    {
        var toBase = Rate(quote, input.BaseCurrency, quoteToBase);
        var toAccount = Rate(quote, input.AccountCurrency, quoteToAccount);
        return new Valued(instrumentId, symbol, quote, quantity, price, toBase, toAccount, quantity * price * toBase, weight);
    }

    private static bool SkipForDrift(RebalanceInput input, decimal total, decimal currentValue, decimal targetValue)
    {
        if (total == 0m)
        {
            return false;
        }

        var currentShare = currentValue / total * 100m;
        var targetShare = targetValue / total * 100m;
        var absolute = Math.Abs(currentShare - targetShare);
        var relative = targetShare == 0m ? 100m : absolute / targetShare * 100m;
        return absolute < input.DriftAbsolute || relative < input.DriftRelative;
    }

    private static decimal Rate(string from, string to, decimal? supplied) =>
        string.Equals(from, to, StringComparison.OrdinalIgnoreCase) ? 1m : supplied!.Value;

    private static RebalanceTrade Sell(Valued row, decimal quantity) =>
        new(row.InstrumentId, row.Symbol, false, quantity, row.Price, row.Quote, row.ToAccount);

    private static RebalanceTrade Buy(Valued row, decimal quantity) =>
        new(row.InstrumentId, row.Symbol, true, quantity, row.Price, row.Quote, row.ToAccount);

    private sealed record Valued(
        int InstrumentId,
        string Symbol,
        string Quote,
        decimal Quantity,
        decimal Price,
        decimal ToBase,
        decimal ToAccount,
        decimal MarketValueBase,
        decimal Weight);
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
