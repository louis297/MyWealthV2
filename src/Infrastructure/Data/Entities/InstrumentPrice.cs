namespace MyWealthV2.Infrastructure.Data.Entities;

public class InstrumentPrice
{
    public int InstrumentId { get; set; }

    public decimal Price { get; set; }

    public DateOnly AsOfDate { get; set; }

    public string Source { get; set; } = string.Empty;

    public DateTimeOffset FetchedAt { get; set; }
}
