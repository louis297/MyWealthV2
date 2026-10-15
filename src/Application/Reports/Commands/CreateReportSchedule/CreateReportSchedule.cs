using System.Text.Json;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Reports.Commands.CreateReportSchedule;

[Authorize(Policy = Policies.ReportsCustomerCreate + "," + Policies.ReportsBookCreate)]
public class CreateReportScheduleCommand : IRequest<Guid>
{
    public Guid ReportId { get; set; }

    public JsonElement Parameters { get; set; }

    public string? Cron { get; set; }

    public bool? IsActive { get; set; }
}

public class CreateReportScheduleCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IReportScheduler scheduler)
    : IRequestHandler<CreateReportScheduleCommand, Guid>
{
    public async Task<Guid> Handle(CreateReportScheduleCommand request, CancellationToken cancellationToken)
    {
        ReportScheduleRules.EnsureShape(request.ReportId, request.Parameters, request.Cron, request.IsActive);
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var report = await ReportScheduleRules.EnsureAvailableAsync(
            db, currentUser, request.ReportId, cancellationToken);

        var binding = CustomerTransactionsBinder.Bind(request.Parameters);
        var schedule = ReportSchedule.Create(
            tenantId,
            report.Id,
            binding.SnapshotJson,
            request.IsActive ?? false,
            request.Cron ?? string.Empty);
        db.ReportSchedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);
        scheduler.AddOrUpdateRecurring(schedule.PublicId, schedule.Cron);
        return schedule.PublicId;
    }
}
