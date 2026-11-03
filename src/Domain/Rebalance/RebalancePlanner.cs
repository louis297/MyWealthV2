namespace MyWealthV2.Domain.Rebalance;

public static class RebalancePlanner
{
    public static RebalancePlan Plan(RebalanceInput input)
    {
        var holdings = input.Holdings.Where(holding => holding.Quantity > 0m).ToList();
        foreach (var holding in holdings)
        {
            if (QuoteProblem(holding.Symbol, holding.QuoteCurrency, holding.Price, holding.QuoteToBase, holding.QuoteToAccount, input) is { } problem)
            {
                return problem;
            }
        }

        foreach (var target in input.Targets)
        {
            if (QuoteProblem(target.Symbol, target.QuoteCurrency, target.Price, target.QuoteToBase, target.QuoteToAccount, input) is { } problem)
            {
                return problem;
            }
        }

        if (!Same(input.AccountCurrency, input.BaseCurrency) && input.AccountToBase is null)
        {
            return Fail("missing_fx", $"FX rate is not available for {input.AccountCurrency}/{input.BaseCurrency}.");
        }

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
            if (delta == 0m || Math.Abs(delta) < input.MinimumTrade)
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

        if (trades.Count == 0)
        {
            return new RebalancePlan(RebalanceStatus.NoChange, null, null, []);
        }

        // Post every sell before any buy. Cash from an overweight line has to land
        // before an underweight buy, or the buy fails while the balance is temporarily negative.
        trades = trades.Where(trade => !trade.Buy).Concat(trades.Where(trade => trade.Buy)).ToList();

        var cash = input.Cash;
        foreach (var trade in trades)
        {
            cash += trade.Buy
                ? -RoundMoney(RoundMoney(trade.Quantity * trade.Price) * trade.QuoteToAccount)
                : RoundMoney(trade.Quantity * trade.Price * trade.QuoteToAccount);
        }

        if (cash < 0m)
        {
            var accountShortfall = -cash;
            var baseShortfall = accountShortfall * Rate(input.AccountCurrency, input.BaseCurrency, input.AccountToBase);
            var quote = trades.Last(trade => trade.Buy).QuoteCurrency;
            return Fail(
                "insufficient_cash",
                $"Shortfall {baseShortfall:0.00} {input.BaseCurrency} ({accountShortfall:0.00} {input.AccountCurrency}) quote {quote}.");
        }

        return new RebalancePlan(RebalanceStatus.Success, null, null, trades);
    }

    private static RebalancePlan? QuoteProblem(
        string symbol,
        string quote,
        decimal? price,
        decimal? quoteToBase,
        decimal? quoteToAccount,
        RebalanceInput input)
    {
        if (price is null or <= 0m)
        {
            return Fail("missing_price", $"Price is not available for {symbol}.");
        }

        if (!Same(quote, input.BaseCurrency) && quoteToBase is null)
        {
            return Fail("missing_fx", $"FX rate is not available for {quote}/{input.BaseCurrency}.");
        }

        if (!Same(quote, input.AccountCurrency) && quoteToAccount is null)
        {
            return Fail("missing_fx", $"FX rate is not available for {quote}/{input.AccountCurrency}.");
        }

        return null;
    }

    private static RebalancePlan Fail(string code, string message) =>
        new(RebalanceStatus.Failed, code, message, []);

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 4, MidpointRounding.AwayFromZero);

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

    private static decimal Rate(string from, string to, decimal? supplied) =>
        Same(from, to) ? 1m : supplied!.Value;

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
