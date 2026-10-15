using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports.Commands.UpdateReportSchedule;

[Authorize(Policy = Policies.ReportsCustomerCreate + "," + Policies.ReportsBookCreate)]
public class UpdateReportScheduleCommand : IRequest
{
    public Guid Id { get; set; }

    public Guid ReportId { get; set; }

    public JsonElement Parameters { get; set; }

    public string? Cron { get; set; }

    public bool? IsActive { get; set; }

    public string? RowVersion { get; set; }
}

public class UpdateReportScheduleCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IReportScheduler scheduler)
    : IRequestHandler<UpdateReportScheduleCommand>
{
    public async Task Handle(UpdateReportScheduleCommand request, CancellationToken cancellationToken)
    {
        ReportScheduleRules.EnsureShape(request.ReportId, request.Parameters, request.Cron, request.IsActive);
        if (string.IsNullOrWhiteSpace(request.RowVersion))
        {
            throw new ValidationException([
                new ValidationFailure("rowVersion", "rowVersion is required.")
            ]);
        }

        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var schedule = await db.ReportSchedules
                           .SingleOrDefaultAsync(
                               row => row.PublicId == request.Id && row.TenantId == tenantId,
                               cancellationToken)
                       ?? throw new NotFoundException("ReportSchedule", request.Id);

        var expected = Convert.ToBase64String(schedule.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        var report = await ReportScheduleRules.EnsureAvailableAsync(
            db, currentUser, request.ReportId, cancellationToken);
        if (report.Id != schedule.ReportId)
        {
            throw new NotFoundException("Report", request.ReportId);
        }

        string snapshotJson;
        if (report.Code == AdviserBalancesBinder.Code)
        {
            var role = currentUser.Role ?? throw new UnauthorizedAccessException();
            await AdviserReportScope.EnsureSnapshotAsync(
                db, currentUser, schedule.ParametersJson, "ReportSchedule", cancellationToken);
            var binding = AdviserBalancesBinder.Bind(request.Parameters, role, currentUser.PublicId);
            await AdviserReportScope.EnsureAsync(
                db,
                tenantId,
                binding.AdviserId,
                role,
                currentUser.PublicId,
                cancellationToken);
            snapshotJson = binding.SnapshotJson;
        }
        else
        {
            var binding = CustomerTransactionsBinder.Bind(request.Parameters);
            await ReportScheduleRules.EnsureCustomerScopeAsync(
                db, currentUser, binding.CustomerId, cancellationToken);
            snapshotJson = binding.SnapshotJson;
        }

        schedule.Revise(snapshotJson, request.IsActive!.Value, request.Cron!);
        await db.SaveChangesAsync(cancellationToken);
        scheduler.AddOrUpdateRecurring(schedule.PublicId, schedule.Cron);
    }
}
