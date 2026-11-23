using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Fees.TradeSchedules;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Rebalances;

namespace MyWealthV2.Application.Rebalances;

/// <summary>The targets and settings a plan is made against: a portfolio's, or a rebalance's own (rebalances R7,
/// R10).</summary>
public sealed record RebalanceTargets(
    string BaseCurrency,
    decimal MinimumTrade,
    decimal CashResidualPercent,
    IReadOnlyList<RebalanceTargetSpec> Lines)
{
    public static RebalanceTargets Of(Portfolio portfolio) => new(
        portfolio.BaseCurrency,
        portfolio.MinimumTrade,
        portfolio.CashResidualPercent,
        portfolio.Lines.OrderBy(line => line.SortOrder)
            .Select(line => new RebalanceTargetSpec(line.InstrumentId, line.TargetWeight))
            .ToList());

    public static RebalanceTargets Of(Rebalance rebalance) => new(
        rebalance.BaseCurrency,
        rebalance.MinimumTrade,
        rebalance.CashResidualPercent,
        rebalance.Targets.OrderBy(target => target.SortOrder)
            .Select(target => new RebalanceTargetSpec(target.InstrumentId, target.TargetWeight))
            .ToList());
}

/// <summary>A plan and what it was made from: the available cash, <c>V</c>, the quoted holdings and targets, the
/// price dates and the fee schedules of the UTC date.</summary>
public sealed record PlannedRebalance(
    RebalancePlan Plan,
    decimal Cash,
    decimal? TotalValue,
    IReadOnlyList<PlannerHolding> Holdings,
    IReadOnlyList<PlannerTarget> Targets,
    IReadOnlyDictionary<string, PlannerFeeSchedule> FeeSchedules,
    decimal? AccountToBase)
{
    public bool Failed => Plan.Status == RebalancePlanOutcome.Failed;

    /// <summary>R9: one line per Instrument held or targeted, with the trade the plan makes at the stored price.</summary>
    public IReadOnlyList<RebalanceLineSpec> Lines()
    {
        var targets = Targets.ToDictionary(target => target.InstrumentId);
        var held = Holdings.Where(holding => holding.Quantity > 0m).ToDictionary(holding => holding.InstrumentId);
        var trades = Plan.Trades.ToDictionary(trade => trade.InstrumentId);
        var ids = held.Keys.Concat(Targets.Select(target => target.InstrumentId)).Distinct().ToList();
        return ids.Select(id =>
        {
            held.TryGetValue(id, out var holding);
            targets.TryGetValue(id, out var target);
            var quote = holding?.QuoteCurrency ?? target!.QuoteCurrency;
            var quantity = holding?.Quantity ?? 0m;
            var price = holding?.Price ?? target?.Price;
            // A null rate means the quote is the base currency (the plan failed on any other missing rate).
            var toBase = holding?.QuoteToBase ?? target?.QuoteToBase ?? 1m;
            var weight = TotalValue is > 0m && price is { } p
                ? decimal.Round(quantity * p * toBase / TotalValue.Value * 100m, 2, MidpointRounding.AwayFromZero)
                : 0m;
            RebalanceLineTrade? trade = null;
            if (trades.TryGetValue(id, out var planned))
            {
                var gross = decimal.Round(planned.Quantity * planned.Price, 4, MidpointRounding.AwayFromZero);
                FeeSchedules.TryGetValue(planned.QuoteCurrency, out var fee);
                trade = new RebalanceLineTrade(
                    planned.Buy ? OrderSide.Buy : OrderSide.Sell,
                    planned.Quantity,
                    planned.Price,
                    (holding?.PriceDate ?? target?.PriceDate)!.Value,
                    gross,
                    fee?.Schedule.FeeFor(gross, fee.DecimalPlaces).Fee ?? 0m);
            }

            return new RebalanceLineSpec(id, quantity, weight, target?.TargetWeight, quote, trade);
        }).ToList();
    }
}

/// <summary>
/// Builds the input of <see cref="RebalancePlanner"/> for one Custodial account against the targets it is given, with
/// the checks orders add (rebalances R7, R8): the latest stored prices and their dates, the stored rates, the available
/// cash (withdrawals R35) and the trade fee schedules of the UTC date. With <c>sizeBuysByReservation</c> the buys are
/// sized by the cash their orders reserve (R16).
/// </summary>
public sealed class RebalancePlanning(
    IApplicationDbContext db,
    IMarketData marketData,
    IFxRate fxRates,
    ICurrencyCatalog catalog,
    IInstrumentPriceStore prices)
{
    public async Task<PlannedRebalance> PlanAsync(
        Account account,
        RebalanceTargets targets,
        bool sizeBuysByReservation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var holdingRows = await (
            from holding in db.Holdings.IgnoreQueryFilters()
            join instrument in db.Instruments.IgnoreQueryFilters() on holding.InstrumentId equals instrument.Id
            where holding.AccountId == account.Id && holding.Quantity > 0m
            orderby instrument.Symbol
            select new { holding.Quantity, instrument }).ToListAsync(cancellationToken);
        var targetIds = targets.Lines.Select(line => line.InstrumentId).ToList();
        var lineInstruments = await db.Instruments.IgnoreQueryFilters()
            .Where(instrument => targetIds.Contains(instrument.Id))
            .ToDictionaryAsync(instrument => instrument.Id, cancellationToken);

        var holdings = new List<PlannerHolding>();
        foreach (var row in holdingRows)
        {
            holdings.Add(await QuoteAsync(row.instrument, row.Quantity, account.Currency, targets.BaseCurrency, today, cancellationToken));
        }

        var planned = new List<PlannerTarget>();
        foreach (var line in targets.Lines)
        {
            var quoted = await QuoteAsync(lineInstruments[line.InstrumentId], 0m, account.Currency, targets.BaseCurrency, today, cancellationToken);
            planned.Add(new PlannerTarget(
                quoted.InstrumentId, quoted.Symbol, quoted.QuoteCurrency, line.TargetWeight, quoted.Price,
                quoted.QuoteToBase, quoted.QuoteToAccount, quoted.IsActive, quoted.PriceDate));
        }

        var accountToBase = await RateAsync(account.Currency, targets.BaseCurrency, cancellationToken);
        var cash = await AccountCash.AvailableAsync(db, account.Id, cancellationToken);
        var feeSchedules = await FeeSchedulesAsync(
            account.TenantId,
            holdings.Select(row => row.QuoteCurrency).Concat(planned.Select(row => row.QuoteCurrency)),
            now,
            cancellationToken);
        var plan = RebalancePlanner.Plan(new RebalanceInput(
            targets.BaseCurrency,
            account.Currency,
            cash,
            accountToBase,
            targets.MinimumTrade,
            targets.CashResidualPercent,
            holdings,
            planned,
            feeSchedules,
            OrderChecks: true,
            SizeBuysByReservation: sizeBuysByReservation,
            Today: today));

        return new PlannedRebalance(
            plan, cash, TotalValue(account.Currency, targets.BaseCurrency, cash, accountToBase, holdings), holdings, planned,
            feeSchedules, accountToBase);
    }

    /// <summary>R7: <c>V</c>, the available cash plus the market value of the holdings in the base currency; null when
    /// a price or a rate is missing.</summary>
    private static decimal? TotalValue(
        string accountCurrency,
        string baseCurrency,
        decimal cash,
        decimal? accountToBase,
        IReadOnlyList<PlannerHolding> holdings)
    {
        var cashRate = Same(accountCurrency, baseCurrency) ? 1m : accountToBase;
        if (cashRate is null)
        {
            return null;
        }

        var total = cash * cashRate.Value;
        foreach (var holding in holdings)
        {
            var rate = Same(holding.QuoteCurrency, baseCurrency) ? 1m : holding.QuoteToBase;
            if (holding.Price is null || rate is null)
            {
                return null;
            }

            total += holding.Quantity * holding.Price.Value * rate.Value;
        }

        return decimal.Round(total, 4, MidpointRounding.AwayFromZero);
    }

    private async Task<IReadOnlyDictionary<string, PlannerFeeSchedule>> FeeSchedulesAsync(
        int tenantId,
        IEnumerable<string> quoteCurrencies,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, PlannerFeeSchedule>(StringComparer.OrdinalIgnoreCase);
        var date = ApplicableTradeFeeSchedule.DateOf(now);
        foreach (var quote in quoteCurrencies.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var schedule = await ApplicableTradeFeeSchedule.ForAsync(db, tenantId, quote, date, cancellationToken);
            if (schedule is not null)
            {
                result[quote] = new PlannerFeeSchedule(schedule, catalog.TryGet(quote)?.DecimalPlaces ?? 2);
            }
        }

        return result;
    }

    private async Task<PlannerHolding> QuoteAsync(
        Instrument instrument,
        decimal quantity,
        string accountCurrency,
        string baseCurrency,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var price = marketData.TryGetPrice(instrument.Id, null);
        var quote = instrument.QuoteCurrency.Trim().ToUpperInvariant();
        decimal? amount = price is null || price.Amount <= 0m
            || !string.Equals(price.Currency, quote, StringComparison.OrdinalIgnoreCase)
            ? null
            : price.Amount;
        var priceDate = amount is null ? null : await prices.LatestDateAsync(instrument.Id, today, cancellationToken);
        return new PlannerHolding(
            instrument.Id,
            instrument.Symbol,
            quote,
            quantity,
            amount,
            await RateAsync(quote, baseCurrency, cancellationToken),
            await RateAsync(quote, accountCurrency, cancellationToken),
            instrument.IsActive,
            priceDate);
    }

    private async Task<decimal?> RateAsync(string from, string to, CancellationToken cancellationToken)
    {
        if (Same(from, to))
        {
            return null;
        }

        try
        {
            return await fxRates.GetRateAsync(from.Trim(), to.Trim(), null, cancellationToken);
        }
        catch (FxRateNotFoundException)
        {
            return null;
        }
    }

    private static bool Same(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
