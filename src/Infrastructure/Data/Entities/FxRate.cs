namespace MyWealthV2.Infrastructure.Data.Entities;

public class FxRate
{
    public string BaseCurrency { get; set; } = string.Empty;

    public string QuoteCurrency { get; set; } = string.Empty;

    public decimal Rate { get; set; }

    public DateOnly AsOfDate { get; set; }

    public string Source { get; set; } = string.Empty;

    public DateTimeOffset FetchedAt { get; set; }
}
