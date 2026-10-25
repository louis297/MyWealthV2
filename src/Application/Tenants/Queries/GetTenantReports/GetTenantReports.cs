using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Reports;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Tenants.Queries.GetTenantReports;

[Authorize(Policy = Policies.TenantsManage)]
public record GetTenantReportsQuery(Guid TenantId) : IRequest<IReadOnlyList<TenantReportGrantDto>>;

public class GetTenantReportsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetTenantReportsQuery, IReadOnlyList<TenantReportGrantDto>>
{
    public async Task<IReadOnlyList<TenantReportGrantDto>> Handle(
        GetTenantReportsQuery request,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking()
                         .SingleOrDefaultAsync(row => row.PublicId == request.TenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant", request.TenantId);

        var reports = await db.Reports.AsNoTracking()
            .OrderBy(report => report.Code)
            .ToListAsync(cancellationToken);
        var grantedReportIds = (await db.TenantReports.AsNoTracking()
            .Where(grant => grant.TenantId == tenant.Id)
            .Select(grant => grant.ReportId)
            .ToListAsync(cancellationToken)).ToHashSet();

        return reports.Select(report => new TenantReportGrantDto
        {
            Id = report.PublicId,
            Code = report.Code,
            Name = report.Name,
            Function = report.Function,
            Scope = report.Scope,
            AllowedTriggers = TenantReportGrantDto.ParseTriggers(report.AllowedTriggers),
            IsActive = report.IsActive,
            IsGranted = grantedReportIds.Contains(report.Id)
        }).ToList();
    }
}
