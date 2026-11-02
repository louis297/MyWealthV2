using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Portfolios;

public sealed class PortfolioLineDto
{
    public required Guid InstrumentId { get; init; }

    public required decimal TargetWeight { get; init; }

    public required int SortOrder { get; init; }
}

public sealed class PortfolioDto
{
    public required Guid Id { get; init; }

    public required Guid TenantId { get; init; }

    public required string Name { get; init; }

    public required string BaseCurrency { get; init; }

    public required bool IsActive { get; init; }

    public required decimal DriftAbsolute { get; init; }

    public required decimal DriftRelative { get; init; }

    public required decimal MinimumTrade { get; init; }

    public required decimal CashResidualPercent { get; init; }

    public required string RowVersion { get; init; }

    public required IReadOnlyList<PortfolioLineDto> Lines { get; init; }

    public static PortfolioDto From(
        Portfolio portfolio,
        Tenant tenant,
        IReadOnlyDictionary<int, Guid> instrumentPublicIds) => new()
    {
        Id = portfolio.PublicId,
        TenantId = tenant.PublicId,
        Name = portfolio.Name,
        BaseCurrency = portfolio.BaseCurrency,
        IsActive = portfolio.IsActive,
        DriftAbsolute = portfolio.DriftAbsolute,
        DriftRelative = portfolio.DriftRelative,
        MinimumTrade = portfolio.MinimumTrade,
        CashResidualPercent = portfolio.CashResidualPercent,
        RowVersion = Convert.ToBase64String(portfolio.RowVersion ?? []),
        Lines = portfolio.Lines
            .OrderBy(line => line.SortOrder)
            .Select(line => new PortfolioLineDto
            {
                InstrumentId = instrumentPublicIds[line.InstrumentId],
                TargetWeight = line.TargetWeight,
                SortOrder = line.SortOrder
            })
            .ToList()
    };
}
