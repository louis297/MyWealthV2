using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Tenants.Commands.RevokeTenantReport;

[Authorize(Policy = Policies.TenantsManage)]
public record RevokeTenantReportCommand(Guid TenantId, Guid ReportId) : IRequest;

public class RevokeTenantReportCommandHandler(IApplicationDbContext db)
    : IRequestHandler<RevokeTenantReportCommand>
{
    public async Task Handle(RevokeTenantReportCommand request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking()
                         .SingleOrDefaultAsync(row => row.PublicId == request.TenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant", request.TenantId);
        var report = await db.Reports.AsNoTracking()
                         .SingleOrDefaultAsync(row => row.PublicId == request.ReportId, cancellationToken)
                     ?? throw new NotFoundException("Report", request.ReportId);

        var grant = await db.TenantReports
            .SingleOrDefaultAsync(
                row => row.TenantId == tenant.Id && row.ReportId == report.Id,
                cancellationToken);
        if (grant is null)
        {
            return;
        }

        db.TenantReports.Remove(grant);
        await db.SaveChangesAsync(cancellationToken);
    }
}
