using System.Text;
using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports.Commands.RequestReport;

[Authorize(Policy = Policies.ReportsCustomerCreate + "," + Policies.ReportsBookCreate)]
public class RequestReportCommand : IRequest<Guid>
{
    public Guid ReportId { get; set; }

    public JsonElement Parameters { get; set; }

    public string? Cron { get; set; }

    public bool? IsActive { get; set; }
}

public class RequestReportCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IReportScheduler scheduler,
    TimeProvider time)
    : IRequestHandler<RequestReportCommand, Guid>
{
    public const int MaxParametersBytes = 8 * 1024;

    public async Task<Guid> Handle(RequestReportCommand request, CancellationToken cancellationToken)
    {
        if (request.Parameters.ValueKind != JsonValueKind.Object)
        {
            throw new ValidationException([
                new ValidationFailure("parameters", "parameters must be an object.")
            ]);
        }

        if (Encoding.UTF8.GetByteCount(request.Parameters.GetRawText()) > MaxParametersBytes)
        {
            throw new ValidationException([
                new ValidationFailure("parameters", "parameters must be 8 KB or smaller.")
            ]);
        }

        var role = currentUser.Role ?? throw new UnauthorizedAccessException();
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var report = await db.Reports.AsNoTracking()
                         .SingleOrDefaultAsync(row => row.PublicId == request.ReportId, cancellationToken)
                     ?? throw new NotFoundException("Report", request.ReportId);

        var granted = await db.TenantReports.AsNoTracking()
            .AnyAsync(row => row.TenantId == tenantId && row.ReportId == report.Id, cancellationToken);
        var allowsManual = TenantReportGrantDto.ParseTriggers(report.AllowedTriggers)
            .Contains("manual", StringComparer.Ordinal);
        if (!report.IsActive || !granted || !allowsManual)
        {
            throw new NotFoundException("Report", report.PublicId);
        }

        var createPolicy = report.Scope == "Book"
            ? Policies.ReportsBookCreate
            : Policies.ReportsCustomerCreate;
        if (!RolePermissions.Has(role, createPolicy))
        {
            throw new ForbiddenAccessException();
        }

        string snapshotJson;
        DateTimeOffset? asOf = null;
        if (report.Code == CustomerTransactionsBinder.Code)
        {
            var binding = CustomerTransactionsBinder.Bind(request.Parameters);
            await EnsureCustomerInScopeAsync(binding.CustomerId, tenantId, role, cancellationToken);
            snapshotJson = binding.SnapshotJson;
        }
        else if (report.Code == AdviserBalancesBinder.Code)
        {
            var binding = AdviserBalancesBinder.Bind(request.Parameters, role, currentUser.PublicId);
            await AdviserReportScope.EnsureAsync(
                db, tenantId, binding.AdviserId, role, currentUser.PublicId, cancellationToken);
            snapshotJson = binding.SnapshotJson;
            asOf = time.GetUtcNow();
        }
        else
        {
            throw new NotFoundException("Report", report.PublicId);
        }

        var execution = ReportExecution.Queue(
            tenantId,
            report.Id,
            "Manual",
            snapshotJson,
            currentUser.Person?.Id,
            asOf);
        db.ReportExecutions.Add(execution);
        await db.SaveChangesAsync(cancellationToken);
        scheduler.Enqueue(execution.PublicId);
        return execution.PublicId;
    }

    private async Task EnsureCustomerInScopeAsync(
        Guid customerId,
        int tenantId,
        UserRole role,
        CancellationToken cancellationToken)
    {
        var customer = await db.Users.AsNoTracking()
                           .SingleOrDefaultAsync(
                               row => row.PublicId == customerId
                                      && row.TenantId == tenantId
                                      && row.Role == UserRole.Customer,
                               cancellationToken)
                       ?? throw new NotFoundException("Customer", customerId);

        CustomerReportScope.Ensure(customer, role, currentUser);
    }
}
