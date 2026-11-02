namespace MyWealthV2.Domain.Entities;

public class PortfolioLine : BaseEntity
{
    private PortfolioLine()
    {
    }

    public int PortfolioId { get; private set; }

    public int InstrumentId { get; private set; }

    public decimal TargetWeight { get; private set; }

    public int SortOrder { get; private set; }

    internal static PortfolioLine Create(int instrumentId, decimal targetWeight, int sortOrder) =>
        new()
        {
            InstrumentId = instrumentId,
            TargetWeight = targetWeight,
            SortOrder = sortOrder
        };
}
