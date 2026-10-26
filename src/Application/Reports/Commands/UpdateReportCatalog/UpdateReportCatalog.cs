using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Reports.Commands.UpdateReportCatalog;

[Authorize(Policy = Policies.TenantsManage)]
public class UpdateReportCatalogCommand : IRequest
{
    public Guid Id { get; set; }

    public bool? IsActive { get; init; }

    public IReadOnlyList<string>? AllowedTriggers { get; init; }
}

public class UpdateReportCatalogCommandValidator : AbstractValidator<UpdateReportCatalogCommand>
{
    public UpdateReportCatalogCommandValidator()
    {
        RuleFor(command => command)
            .Must(command => command.IsActive is not null || command.AllowedTriggers is not null)
            .WithMessage("isActive or allowedTriggers is required.");
    }
}

public class UpdateReportCatalogCommandHandler(IApplicationDbContext db, IReportScheduler scheduler)
    : IRequestHandler<UpdateReportCatalogCommand>
{
    public async Task Handle(UpdateReportCatalogCommand request, CancellationToken cancellationToken)
    {
        var report = await db.Reports
                         .SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                     ?? throw new NotFoundException("Report", request.Id);

        if (request.IsActive is not bool isActive || isActive == report.IsActive)
        {
            return;
        }

        report.SetIsActive(isActive);
        if (isActive)
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var grants = await db.TenantReports
            .Where(grant => grant.ReportId == report.Id)
            .ToListAsync(cancellationToken);
        var scheduleIds = await db.ReportSchedules.AsNoTracking()
            .Where(row => row.ReportId == report.Id)
            .Select(row => row.PublicId)
            .ToListAsync(cancellationToken);
        db.TenantReports.RemoveRange(grants);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var scheduleId in scheduleIds)
        {
            scheduler.RemoveRecurring(scheduleId);
        }
    }
}
