using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Reports;

public sealed class ReportJob(
    IApplicationDbContext db,
    IReportGeneratorRegistry generators,
    IReportFileStore files)
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

    public Task RunScheduled(Guid scheduleId)
    {
        _ = scheduleId;
        return Task.CompletedTask;
    }
}
