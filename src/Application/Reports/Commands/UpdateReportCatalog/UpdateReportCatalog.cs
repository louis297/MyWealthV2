using System.Text.Json;
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
    private static readonly HashSet<string> KnownTriggers = new(StringComparer.Ordinal)
    {
        "manual",
        "scheduled",
        "event"
    };

    public UpdateReportCatalogCommandValidator()
    {
        RuleFor(command => command)
            .Must(command => command.IsActive is not null || command.AllowedTriggers is not null)
            .WithMessage("isActive or allowedTriggers is required.");
        RuleFor(command => command.AllowedTriggers)
            .Must(triggers => triggers is null
                || (triggers.All(KnownTriggers.Contains)
                    && triggers.Distinct(StringComparer.Ordinal).Count() == triggers.Count))
            .WithMessage("allowedTriggers must be manual, scheduled, or event.");
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

        var removeJobs = new List<Guid>();
        var changed = false;

        if (request.AllowedTriggers is { } triggers)
        {
            var next = JsonSerializer.Serialize(triggers);
            var current = TenantReportGrantDto.ParseTriggers(report.AllowedTriggers)
                .ToHashSet(StringComparer.Ordinal);
            var nextSet = triggers.ToHashSet(StringComparer.Ordinal);
            var removingScheduled = current.Contains("scheduled") && !nextSet.Contains("scheduled");
            if (!string.Equals(next, report.AllowedTriggers, StringComparison.Ordinal))
            {
                report.SetAllowedTriggers(next);
                changed = true;
                if (removingScheduled)
                {
                    var scheduleIds = await db.ReportSchedules.AsNoTracking()
                        .Where(row => row.ReportId == report.Id)
                        .Select(row => row.PublicId)
                        .ToListAsync(cancellationToken);
                    await db.ReportSchedules
                        .Where(row => row.ReportId == report.Id)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(row => row.IsActive, false),
                            cancellationToken);
                    removeJobs.AddRange(scheduleIds);
                }
            }
        }

        if (request.IsActive is bool isActive && isActive != report.IsActive)
        {
            report.SetIsActive(isActive);
            changed = true;
            if (!isActive)
            {
                var grants = await db.TenantReports
                    .Where(grant => grant.ReportId == report.Id)
                    .ToListAsync(cancellationToken);
                db.TenantReports.RemoveRange(grants);
                var scheduleIds = await db.ReportSchedules.AsNoTracking()
                    .Where(row => row.ReportId == report.Id)
                    .Select(row => row.PublicId)
                    .ToListAsync(cancellationToken);
                removeJobs.AddRange(scheduleIds);
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var scheduleId in removeJobs.Distinct())
        {
            scheduler.RemoveRecurring(scheduleId);
        }
    }
}
