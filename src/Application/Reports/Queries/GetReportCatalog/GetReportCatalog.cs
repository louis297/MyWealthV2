using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Reports.Queries.GetReportCatalog;

[Authorize(Policy = Policies.TenantsManage)]
public record GetReportCatalogQuery : IRequest<IReadOnlyList<PlatformReportDto>>;

public sealed class PlatformReportDto
{
    public required Guid Id { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Function { get; init; }

    public required string Scope { get; init; }

    public required IReadOnlyList<string> AllowedTriggers { get; init; }

    public required bool IsActive { get; init; }
}

public class GetReportCatalogQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetReportCatalogQuery, IReadOnlyList<PlatformReportDto>>
{
    public async Task<IReadOnlyList<PlatformReportDto>> Handle(
        GetReportCatalogQuery request,
        CancellationToken cancellationToken)
    {
        var reports = await db.Reports.AsNoTracking()
            .OrderBy(report => report.Code)
            .ToListAsync(cancellationToken);

        return reports.Select(report => new PlatformReportDto
        {
            Id = report.PublicId,
            Code = report.Code,
            Name = report.Name,
            Function = report.Function,
            Scope = report.Scope,
            AllowedTriggers = TenantReportGrantDto.ParseTriggers(report.AllowedTriggers),
            IsActive = report.IsActive
        }).ToList();
    }
}
