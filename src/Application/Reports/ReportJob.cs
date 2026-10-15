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
        var generator = generators.Find(report.Code)
                        ?? throw new InvalidOperationException("Report generator is not available.");
        var pdf = await generator.GenerateAsync(execution, CancellationToken.None);
        var tenantPublicId = await db.Tenants.AsNoTracking()
            .Where(row => row.Id == execution.TenantId)
            .Select(row => row.PublicId)
            .SingleAsync();
        var key = $"{tenantPublicId:D}/{execution.PublicId:D}.pdf";
        await files.SaveAsync(key, pdf, CancellationToken.None);
        execution.Succeed(key);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    public Task RunScheduled(Guid scheduleId)
    {
        _ = scheduleId;
        return Task.CompletedTask;
    }
}
