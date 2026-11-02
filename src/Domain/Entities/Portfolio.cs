namespace MyWealthV2.Domain.Entities;

public class Portfolio : BaseAuditableEntity
{
    private readonly List<PortfolioLine> _lines = [];

    private Portfolio()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string BaseCurrency { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public decimal DriftAbsolute { get; private set; }

    public decimal DriftRelative { get; private set; }

    public decimal MinimumTrade { get; private set; }

    public decimal CashResidualPercent { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<PortfolioLine> Lines => _lines;

    public static Portfolio Create(
        int tenantId,
        string name,
        Currency baseCurrency,
        IReadOnlyList<(int InstrumentId, decimal TargetWeight)> lines,
        Guid? publicId = null)
    {
        _ = tenantId;
        _ = name;
        _ = baseCurrency;
        _ = lines;
        _ = publicId;
        return new Portfolio();
    }
}
