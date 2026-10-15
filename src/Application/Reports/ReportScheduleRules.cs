using System.Text;
using System.Text.Json;
using Cronos;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports;

public static class ReportScheduleRules
{
    public const int MaxParametersBytes = 8 * 1024;

    public const int MaxCronLength = 100;

    public static void EnsureShape(Guid reportId, JsonElement parameters, string? cron, bool? isActive)
    {
        if (reportId == Guid.Empty)
        {
            throw new ValidationException([
                new ValidationFailure("reportId", "reportId is required.")
            ]);
        }

        if (parameters.ValueKind != JsonValueKind.Object)
        {
            throw new ValidationException([
                new ValidationFailure("parameters", "parameters must be an object.")
            ]);
        }

        if (Encoding.UTF8.GetByteCount(parameters.GetRawText()) > MaxParametersBytes)
        {
            throw new ValidationException([
                new ValidationFailure("parameters", "parameters must be 8 KB or smaller.")
            ]);
        }

        if (string.IsNullOrWhiteSpace(cron))
        {
            throw new ValidationException([
                new ValidationFailure("cron", "cron is required.")
            ]);
        }

        if (cron.Length > MaxCronLength)
        {
            throw new ValidationException([
                new ValidationFailure("cron", "cron must be 100 characters or fewer.")
            ]);
        }

        if (!IsStandardCron(cron))
        {
            throw new ValidationException([
                new ValidationFailure("cron", "cron must be a 5-field expression.")
            ]);
        }

        if (isActive is null)
        {
            throw new ValidationException([
                new ValidationFailure("isActive", "isActive is required.")
            ]);
        }
    }

    public static async Task<Report> EnsureAvailableAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        var role = currentUser.Role ?? throw new UnauthorizedAccessException();
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var report = await db.Reports.AsNoTracking()
                         .SingleOrDefaultAsync(row => row.PublicId == reportId, cancellationToken)
                     ?? throw new NotFoundException("Report", reportId);

        var granted = await db.TenantReports.AsNoTracking()
            .AnyAsync(row => row.TenantId == tenantId && row.ReportId == report.Id, cancellationToken);
        var allowsScheduled = TenantReportGrantDto.ParseTriggers(report.AllowedTriggers)
            .Contains("scheduled", StringComparer.Ordinal);
        if (!report.IsActive || !granted || !allowsScheduled)
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

        if (report.Code is not (CustomerTransactionsBinder.Code or AdviserBalancesBinder.Code))
        {
            throw new NotFoundException("Report", report.PublicId);
        }

        return report;
    }

    public static async Task EnsureCustomerScopeAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var role = currentUser.Role ?? throw new UnauthorizedAccessException();
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var customer = await db.Users.AsNoTracking()
                           .SingleOrDefaultAsync(
                               row => row.PublicId == customerId
                                      && row.TenantId == tenantId
                                      && row.Role == UserRole.Customer,
                               cancellationToken)
                       ?? throw new NotFoundException("Customer", customerId);
        CustomerReportScope.Ensure(customer, role, currentUser);
    }

    public static bool IsStandardCron(string cron)
    {
        try
        {
            _ = CronExpression.Parse(cron, CronFormat.Standard);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }
}
