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

        string snapshotJson;
        if (report.Code == AdviserBalancesBinder.Code)
        {
            var role = currentUser.Role ?? throw new UnauthorizedAccessException();
            var binding = AdviserBalancesBinder.Bind(request.Parameters, role, currentUser.PublicId);
            await AdviserReportScope.EnsureAsync(
                db, tenantId, binding.AdviserId, role, currentUser.PublicId, cancellationToken);
            snapshotJson = binding.SnapshotJson;
        }
        else
        {
            var binding = CustomerTransactionsBinder.Bind(request.Parameters);
            await ReportScheduleRules.EnsureCustomerScopeAsync(
                db, currentUser, binding.CustomerId, cancellationToken);
            snapshotJson = binding.SnapshotJson;
        }

        var schedule = ReportSchedule.Create(
            tenantId,
            report.Id,
            snapshotJson,
            request.IsActive ?? false,
            request.Cron ?? string.Empty);
        db.ReportSchedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);
        scheduler.AddOrUpdateRecurring(schedule.PublicId, schedule.Cron);
        return schedule.PublicId;
    }
}
