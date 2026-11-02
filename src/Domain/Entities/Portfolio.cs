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
        _ = publicId;
        var portfolio = new Portfolio();
        portfolio.ReplaceLineWeights(lines);
        return portfolio;
    }

    private void ReplaceLineWeights(IReadOnlyList<(int InstrumentId, decimal TargetWeight)> lines)
    {
        if (lines.Count == 0)
        {
            throw new DomainException("A portfolio needs at least one line.");
        }

        if (lines.Select(line => line.InstrumentId).Distinct().Count() != lines.Count)
        {
            throw new DomainException("A portfolio line instrument is duplicated.");
        }

        var rounded = new decimal[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            var weight = lines[index].TargetWeight;
            if (weight <= 0m || weight > 100m)
            {
                throw new DomainException("Each weight must be greater than 0 and at most 100.");
            }

            rounded[index] = decimal.Round(weight, 2, MidpointRounding.AwayFromZero);
            if (rounded[index] <= 0m || rounded[index] > 100m)
            {
                throw new DomainException("Each weight must be greater than 0 and at most 100.");
            }
        }

        var gap = 100m - rounded.Sum();
        if (gap != 0m && Math.Abs(gap) > 0.01m * lines.Count)
        {
            throw new DomainException("Weights must sum to 100.");
        }

        rounded[^1] += gap;
        if (rounded[^1] <= 0m || rounded[^1] > 100m)
        {
            throw new DomainException("Each weight must be greater than 0 and at most 100.");
        }
        _lines.Clear();
        for (var index = 0; index < lines.Count; index++)
        {
            _lines.Add(PortfolioLine.Create(lines[index].InstrumentId, rounded[index], index));
        }
    }
}
