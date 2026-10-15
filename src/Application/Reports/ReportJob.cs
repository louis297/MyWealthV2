using System.Text.Json;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports;

public sealed class ReportJob(
    IApplicationDbContext db,
    IReportGeneratorRegistry generators,
    IReportFileStore files,
    IReportScheduler scheduler)
{
    public async Task Execute(Guid executionId)
    {
        var execution = await db.ReportExecutions
            .SingleOrDefaultAsync(row => row.PublicId == executionId);
        if (execution is null || execution.Status is "Succeeded" or "Failed")
        {
            return;
        }

        execution.MarkRunning();
        await db.SaveChangesAsync(CancellationToken.None);

        var report = await db.Reports.AsNoTracking()
            .SingleAsync(row => row.Id == execution.ReportId);
        var generator = generators.Find(report.Code);
        if (generator is null)
        {
            execution.Fail("Report generator is not available.");
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        string? writtenKey = null;
        try
        {
            var pdf = await generator.GenerateAsync(execution, CancellationToken.None);
            var tenantPublicId = await db.Tenants.AsNoTracking()
                .Where(row => row.Id == execution.TenantId)
                .Select(row => row.PublicId)
                .SingleAsync();
            writtenKey = $"{tenantPublicId:D}/{execution.PublicId:D}.pdf";
            await files.SaveAsync(writtenKey, pdf, CancellationToken.None);
            execution.Succeed(writtenKey);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            if (writtenKey is not null)
            {
                try
                {
                    await files.DeleteIfExistsAsync(writtenKey, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Recording the generation failure still has to succeed.
                }
            }

            execution.Fail(exception.Message);
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    public async Task RunScheduled(Guid scheduleId)
    {
        var schedule = await db.ReportSchedules
            .SingleOrDefaultAsync(row => row.PublicId == scheduleId);
        if (schedule is null || !schedule.IsActive)
        {
            return;
        }

        var report = await db.Reports.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == schedule.ReportId);
        if (report is null
            || !report.IsActive
            || report.Code != CustomerTransactionsBinder.Code
            || !TenantReportGrantDto.ParseTriggers(report.AllowedTriggers)
                .Contains("scheduled", StringComparer.Ordinal))
        {
            return;
        }

        var granted = await db.TenantReports.AsNoTracking()
            .AnyAsync(row => row.TenantId == schedule.TenantId && row.ReportId == report.Id);
        if (!granted)
        {
            return;
        }

        Guid customerId;
        try
        {
            customerId = CustomerTransactionsBinder.Bind(
                JsonSerializer.Deserialize<JsonElement>(schedule.ParametersJson)).CustomerId;
        }
        catch (ValidationException)
        {
            return;
        }

        var customerExists = await db.Users.AsNoTracking().AnyAsync(row =>
            row.PublicId == customerId
            && row.TenantId == schedule.TenantId
            && row.Role == UserRole.Customer);
        if (!customerExists)
        {
            return;
        }

        var execution = ReportExecution.Queue(
            schedule.TenantId,
            report.Id,
            "Scheduled",
            schedule.ParametersJson,
            triggeredByUserId: null,
            scheduleId: schedule.Id);
        execution.CreatedBy = "system";
        db.ReportExecutions.Add(execution);
        await db.SaveChangesAsync(CancellationToken.None);
        scheduler.Enqueue(execution.PublicId);
    }
}
