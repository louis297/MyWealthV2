using System.Text.Json;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Reports.Commands.DisableReportSchedule;

[Authorize(Policy = Policies.ReportsCustomerCreate + "," + Policies.ReportsBookCreate)]
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

        await ReportScheduleRules.EnsureAvailableAsync(db, currentUser, reportId, cancellationToken);
        var parameters = JsonSerializer.Deserialize<JsonElement>(schedule.ParametersJson);
        var binding = CustomerTransactionsBinder.Bind(parameters);
        await ReportScheduleRules.EnsureCustomerScopeAsync(
            db, currentUser, binding.CustomerId, cancellationToken);

        schedule.Disable();
        await db.SaveChangesAsync(cancellationToken);
        scheduler.RemoveRecurring(schedule.PublicId);
    }
}
