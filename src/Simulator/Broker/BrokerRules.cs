using MyWealthV2.Simulator.Bank;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Broker;

/// <summary>The field rules and the arithmetic of the simulator broker (orders R53–R57, R61).</summary>
public static class BrokerRules
{
    public const int ClientOrderIdMaxLength = 100;
    public const int SymbolMaxLength = 32;
    public const int QuantityDecimalPlaces = 8;
    public const int PriceDecimalPlaces = 4;
    public const int DefaultFillPercent = 100;
    public const decimal DefaultSlippagePercent = 0.10m;
    public const decimal MaxSlippagePercent = 5m;
    public const int DefaultSettlementDays = 2;

    /// <summary>The purpose name of the fill price draw (R56, simulator R26).</summary>
    public const string FillPurpose = "broker-fill";

    /// <summary>The largest number a <c>decimal(18, 8)</c> column holds.</summary>
    private const decimal MaxStoredNumber = 9_999_999_999.99999999m;

    /// <summary>R53: greater than 0 with at most 8 decimal places.</summary>
    public static bool IsNumber(decimal? value) =>
        value is > 0 and <= MaxStoredNumber && decimal.Round(value.Value, QuantityDecimalPlaces) == value.Value;

    /// <summary>R53: every field of an order.</summary>
    public static bool IsOrder(BrokerOrderRequest body) =>
        body.ClientOrderId?.Length is >= 1 and <= ClientOrderIdMaxLength
        && body.Symbol?.Length is >= 1 and <= SymbolMaxLength
        && BankRules.IsCurrency(body.Currency)
        && body.Side is nameof(BrokerOrderSide.Buy) or nameof(BrokerOrderSide.Sell)
        && IsNumber(body.Quantity)
        && IsNumber(body.PriceLimit)
        && IsNumber(body.ReferencePrice)
        && body.Type == "Market"
        && body.TimeInForce == "Day";

    public static bool IsFillPercent(int? percent) => percent is >= 1 and <= 100;

    /// <summary>R56: 0 to 5 with at most 2 decimal places.</summary>
    public static bool IsSlippage(decimal? percent) =>
        percent is >= 0 and <= MaxSlippagePercent && decimal.Round(percent.Value, 2) == percent.Value;

    /// <summary>R55: the part of <paramref name="quantity"/> that mode <c>Fill</c> fills at once, rounded down to 8
    /// decimal places.</summary>
    public static decimal FilledPart(decimal quantity, int fillPercent)
    {
        const decimal scale = 100_000_000m;
        return Math.Truncate(quantity * fillPercent / 100m * scale) / scale;
    }

    /// <summary>R56: <c>Max(0.0001, Round(base × (1 + slip), 4))</c>, rounded half away from zero, with
    /// <c>slip = slippagePercent / 100 × (2u − 1)</c> and <c>u</c> the first draw of (seed, <c>broker-fill</c>,
    /// <paramref name="fillNumber"/>).</summary>
    public static decimal FillPrice(decimal basePrice, decimal slippagePercent, int seed, int fillNumber)
    {
        var u = (decimal)SeededRandom.For(seed, FillPurpose, fillNumber).NextDouble();
        var slip = slippagePercent / 100m * (2m * u - 1m);
        return Math.Max(0.0001m, Math.Round(basePrice * (1m + slip), PriceDecimalPlaces, MidpointRounding.AwayFromZero));
    }

    /// <summary>R61: the trade date plus <paramref name="settlementDays"/> weekdays; Saturday and Sunday are skipped.</summary>
    public static DateOnly SettlementDate(DateOnly tradeDate, int settlementDays)
    {
        var day = tradeDate;
        for (var left = settlementDays; left > 0;)
        {
            day = day.AddDays(1);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                left--;
            }
        }

        return day;
    }
}
