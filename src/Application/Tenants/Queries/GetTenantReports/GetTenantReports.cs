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

        var rows = await (
            from grant in db.TenantReports.AsNoTracking()
            join report in db.Reports.AsNoTracking() on grant.ReportId equals report.Id
            where grant.TenantId == tenant.Id
            orderby report.Code
            select new
            {
                report.PublicId,
                report.Code,
                report.Name,
                report.Function,
                report.Scope,
                report.AllowedTriggers,
                report.IsActive
            }).ToListAsync(cancellationToken);

        return rows.Select(row => new TenantReportGrantDto
        {
            Id = row.PublicId,
            Code = row.Code,
            Name = row.Name,
            Function = row.Function,
            Scope = row.Scope,
            AllowedTriggers = TenantReportGrantDto.ParseTriggers(row.AllowedTriggers),
            IsActive = row.IsActive
        }).ToList();
    }
}
