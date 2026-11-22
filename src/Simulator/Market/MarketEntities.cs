namespace MyWealthV2.Simulator.Market;

/// <summary>A simulated instrument the simulator market owns (simulated-instruments R15). Its own books: it never
/// reads the platform's catalog (R14, R24). <see cref="Price"/> is the price of <see cref="PriceDate"/>, the last
/// simulator day it was priced for.</summary>
public sealed class MarketInstrument
{
    public int Id { get; private set; }

    public string Symbol { get; private set; } = "";

    public string Name { get; private set; } = "";

    public string Currency { get; private set; } = "";

    public decimal DailyMovePercent { get; private set; }

    public decimal Price { get; private set; }

    public DateOnly PriceDate { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public List<MarketPrice> Prices { get; private set; } = [];

    /// <summary>R16, R20: the instrument with its first price, on <paramref name="day"/>.</summary>
    public static MarketInstrument Add(
        string symbol,
        string name,
        string currency,
        decimal price,
        decimal dailyMovePercent,
        DateOnly day,
        bool typed)
    {
        var instrument = new MarketInstrument
        {
            Symbol = symbol,
            Name = name,
            Currency = currency,
            Price = price,
            DailyMovePercent = dailyMovePercent,
            PriceDate = day
        };
        instrument.Prices.Add(new MarketPrice(price, day, typed));
        return instrument;
    }

    /// <summary>R17: the next day's price, stepped from the last one.</summary>
    public MarketPrice StepTo(DateOnly day, decimal price)
    {
        Price = price;
        PriceDate = day;
        return new MarketPrice(price, day, isSet: false) { InstrumentId = Id };
    }

    /// <summary>R19: the operator's price replaces the price of the current day.</summary>
    public void SetPrice(decimal price) => Price = price;

    /// <summary>R19: applies from the next day.</summary>
    public void SetDailyMove(decimal dailyMovePercent) => DailyMovePercent = dailyMovePercent;
}

/// <summary>The price of one instrument on one simulator day (R17, R22).</summary>
public sealed class MarketPrice
{
    private MarketPrice()
    {
    }

    public MarketPrice(decimal price, DateOnly priceDate, bool isSet)
    {
        Price = price;
        PriceDate = priceDate;
        IsSet = isSet;
    }

    public int InstrumentId { get; init; }

    public DateOnly PriceDate { get; private set; }

    public decimal Price { get; private set; }

    /// <summary>A price the operator typed.</summary>
    public bool IsSet { get; private set; }

    public void Set(decimal price)
    {
        Price = price;
        IsSet = true;
    }
}
