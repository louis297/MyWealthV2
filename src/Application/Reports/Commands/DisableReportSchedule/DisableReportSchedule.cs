using System.Text.Json;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Reports.Commands.DisableReportSchedule;

[Authorize(Policy = Policies.ReportsCustomerCreate + "," + Policies.ReportsBookCreate + "," + Policies.ReportsTenantCreate)]
public record DisableReportScheduleCommand(Guid Id) : IRequest;

public class DisableReportScheduleCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IReportScheduler scheduler)
    : IRequestHandler<DisableReportScheduleCommand>
{
    public async Task Handle(DisableReportScheduleCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var schedule = await db.ReportSchedules
                           .SingleOrDefaultAsync(
                               row => row.PublicId == request.Id && row.TenantId == tenantId,
                               cancellationToken)
                       ?? throw new NotFoundException("ReportSchedule", request.Id);
        var reportId = await db.Reports.AsNoTracking()
            .Where(row => row.Id == schedule.ReportId)
            .Select(row => row.PublicId)
            .SingleAsync(cancellationToken);

        var report = await ReportScheduleRules.EnsureAvailableAsync(db, currentUser, reportId, cancellationToken);
        if (report.Code == BankReconciliationBinder.Code)
        {
            // Bank-reconciliation R22: the Tenant scope's policy, checked above, is the whole scope check.
        }
        else if (report.Code == AdviserBalancesBinder.Code)
        {
            await AdviserReportScope.EnsureSnapshotAsync(
                db, currentUser, schedule.ParametersJson, "ReportSchedule", cancellationToken);
        }
        else
        {
            var parameters = JsonSerializer.Deserialize<JsonElement>(schedule.ParametersJson);
            var binding = CustomerTransactionsBinder.Bind(parameters);
            await ReportScheduleRules.EnsureCustomerScopeAsync(
                db, currentUser, binding.CustomerId, cancellationToken);
        }

        schedule.Disable();
        await db.SaveChangesAsync(cancellationToken);
        scheduler.RemoveRecurring(schedule.PublicId);
    }
}
