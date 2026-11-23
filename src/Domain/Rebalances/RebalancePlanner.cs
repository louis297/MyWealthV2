namespace MyWealthV2.Domain.Rebalances;

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
            return new RebalancePlan(RebalancePlanOutcome.NoChange, null, null, []);
        }

        // Post every sell before any buy. Cash from an overweight line has to land
        // before an underweight buy, or the buy fails while the balance is temporarily negative.
        trades = trades.Where(trade => !trade.Buy).Concat(trades.Where(trade => trade.Buy)).ToList();

        // Fees come out of the cash residual first (trade-fees R26). A shortfall scales every buy by one factor k,
        // the largest in [0, 1) that keeps planned cash ≥ 0, found by bisection (R27).
        var sells = trades.Where(trade => !trade.Buy).ToList();
        var buys = trades.Where(trade => trade.Buy).ToList();
        if (PlannedCash(input, sells, buys, 1m) < 0m)
        {
            var atZero = PlannedCash(input, sells, buys, 0m);
            if (atZero < 0m)
            {
                var accountShortfall = -atZero;
                var baseShortfall = accountShortfall * Rate(input.AccountCurrency, input.BaseCurrency, input.AccountToBase);
                var quote = trades.Last().QuoteCurrency;
                return Fail(
                    "insufficient_cash",
                    $"Shortfall {baseShortfall:0.00} {input.BaseCurrency} ({accountShortfall:0.00} {input.AccountCurrency}) quote {quote}.");
            }

            var lo = 0m;
            var hi = 1m;
            for (var step = 0; step < 40; step++)
            {
                var mid = (lo + hi) / 2m;
                if (PlannedCash(input, sells, buys, mid) >= 0m)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            // A scaled buy of zero, or below the minimum trade in base currency, is dropped (R28).
            var toBase = targets.ToDictionary(target => target.InstrumentId, target => target.ToBase);
            var scaled = buys
                .Select(trade => trade with { Quantity = Scale(trade.Quantity, lo) })
                .Where(trade => trade.Quantity > 0m
                                && trade.Quantity * trade.Price * toBase[trade.InstrumentId] >= input.MinimumTrade)
                .ToList();
            trades = sells.Concat(scaled).ToList();
            if (trades.Count == 0)
            {
                return new RebalancePlan(RebalancePlanOutcome.NoChange, null, null, []);
            }
        }

        return new RebalancePlan(RebalancePlanOutcome.Success, null, null, trades);
    }

    /// <summary>Cash after every sell and every buy scaled by <paramref name="k"/>, fees included (R26, R27).</summary>
    private static decimal PlannedCash(
        RebalanceInput input,
        IReadOnlyList<RebalanceTrade> sells,
        IReadOnlyList<RebalanceTrade> buys,
        decimal k)
    {
        var cash = input.Cash;
        foreach (var sell in sells)
        {
            cash += Cash(input, sell, sell.Quantity);
        }

        foreach (var buy in buys)
        {
            var quantity = Scale(buy.Quantity, k);
            if (quantity > 0m)
            {
                cash += Cash(input, buy, quantity);
            }
        }

        return cash;
    }

    private static decimal Cash(RebalanceInput input, RebalanceTrade trade, decimal quantity)
    {
        PlannerFeeSchedule? fee = null;
        input.FeeSchedules?.TryGetValue(trade.QuoteCurrency, out fee);
        return Transaction.TradeCash(
            trade.Buy, quantity, trade.Price, trade.QuoteCurrency, trade.QuoteToAccount, fee?.Schedule, fee?.DecimalPlaces ?? 2);
    }

    private static decimal Scale(decimal quantity, decimal k) =>
        decimal.Round(k * quantity, 8, MidpointRounding.AwayFromZero);

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
        new(RebalancePlanOutcome.Failed, code, message, []);

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
    IReadOnlyList<PlannerTarget> Targets,
    IReadOnlyDictionary<string, PlannerFeeSchedule>? FeeSchedules = null,
    bool OrderChecks = false,
    bool SizeBuysByReservation = false,
    DateOnly? Today = null);

/// <summary>The trade fee schedule that applies on the rebalance's UTC date for one quote currency (trade-fees R25).</summary>
public sealed record PlannerFeeSchedule(TradeFeeSchedule Schedule, int DecimalPlaces);

public sealed record PlannerHolding(
    int InstrumentId,
    string Symbol,
    string QuoteCurrency,
    decimal Quantity,
    decimal? Price,
    decimal? QuoteToBase,
    decimal? QuoteToAccount,
    bool IsActive = true,
    DateOnly? PriceDate = null);

public sealed record PlannerTarget(
    int InstrumentId,
    string Symbol,
    string QuoteCurrency,
    decimal TargetWeight,
    decimal? Price,
    decimal? QuoteToBase,
    decimal? QuoteToAccount,
    bool IsActive = true,
    DateOnly? PriceDate = null);

public sealed record RebalanceTrade(
    int InstrumentId,
    string Symbol,
    bool Buy,
    decimal Quantity,
    decimal Price,
    string QuoteCurrency,
    decimal QuoteToAccount);

public sealed record RebalancePlan(
    RebalancePlanOutcome Status,
    string? Code,
    string? Message,
    IReadOnlyList<RebalanceTrade> Trades);
