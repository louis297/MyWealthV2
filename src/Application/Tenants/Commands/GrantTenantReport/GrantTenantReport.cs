using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Tenants.Commands.GrantTenantReport;

[Authorize(Policy = Policies.TenantsManage)]
public record GrantTenantReportCommand : IRequest<GrantTenantReportResult>
{
    public Guid TenantId { get; set; }

    public Guid ReportId { get; set; }
}

public record GrantTenantReportResult(Guid Id, bool Created);

public sealed record CreatedReportGrantDto(Guid Id);

public class GrantTenantReportCommandHandler(IApplicationDbContext db)
    : IRequestHandler<GrantTenantReportCommand, GrantTenantReportResult>
{
    public async Task<GrantTenantReportResult> Handle(
        GrantTenantReportCommand request,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking()
                         .SingleOrDefaultAsync(row => row.PublicId == request.TenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant", request.TenantId);
        var report = await db.Reports.AsNoTracking()
                         .SingleOrDefaultAsync(row => row.PublicId == request.ReportId, cancellationToken)
                     ?? throw new NotFoundException("Report", request.ReportId);
        if (!report.IsActive)
        {
            throw new NotFoundException("Report", report.PublicId);
        }

        var existing = await db.TenantReports.AsNoTracking()
            .AnyAsync(row => row.TenantId == tenant.Id && row.ReportId == report.Id, cancellationToken);
        if (existing)
        {
            return new GrantTenantReportResult(report.PublicId, Created: false);
        }

        db.TenantReports.Add(TenantReport.Grant(tenant.Id, report.Id));
        await db.SaveChangesAsync(cancellationToken);
        return new GrantTenantReportResult(report.PublicId, Created: true);
    }
}
