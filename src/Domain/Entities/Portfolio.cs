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
        if (baseCurrency is null || string.IsNullOrWhiteSpace(baseCurrency.Code))
        {
            throw new DomainException("Currency is required.");
        }

        var portfolio = new Portfolio
        {
            TenantId = tenantId,
            Name = NormaliseName(name),
            BaseCurrency = baseCurrency.Code.Trim().ToUpperInvariant(),
            PublicId = publicId ?? Guid.NewGuid(),
            IsActive = true,
            DriftAbsolute = 1m,
            DriftRelative = 5m,
            MinimumTrade = 100m,
            CashResidualPercent = 1m
        };
        portfolio.ReplaceLineWeights(lines);
        portfolio.AddDomainEvent(new PortfolioCreated(portfolio));
        return portfolio;
    }

    public void Disable()
    {
        IsActive = false;
    }

    public void ReplaceLines(IReadOnlyList<(int InstrumentId, decimal TargetWeight)> lines)
    {
        var next = BuildLines(lines);
        if (SameWeights(next))
        {
            return;
        }

        _lines.Clear();
        _lines.AddRange(next);
        AddDomainEvent(new PortfolioWeightsChanged(this));
    }

    private static string NormaliseName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < 1 or > 200)
        {
            throw new DomainException("Name is required.");
        }

        return trimmed;
    }

    private void ReplaceLineWeights(IReadOnlyList<(int InstrumentId, decimal TargetWeight)> lines)
    {
        var next = BuildLines(lines);
        _lines.Clear();
        _lines.AddRange(next);
    }

    private bool SameWeights(IReadOnlyList<PortfolioLine> next)
    {
        if (_lines.Count != next.Count)
        {
            return false;
        }

        return _lines.All(line => next.Any(other =>
            other.InstrumentId == line.InstrumentId && other.TargetWeight == line.TargetWeight));
    }

    private static List<PortfolioLine> BuildLines(IReadOnlyList<(int InstrumentId, decimal TargetWeight)> lines)
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

        var built = new List<PortfolioLine>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            built.Add(PortfolioLine.Create(lines[index].InstrumentId, rounded[index], index));
        }

        return built;
    }
}
