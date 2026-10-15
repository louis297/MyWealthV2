using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Reports;

public sealed class ReportJob(IApplicationDbContext db)
{
    public async Task Execute(Guid executionId)
    {
        _ = await db.ReportExecutions
            .AsNoTracking()
            .SingleOrDefaultAsync(execution => execution.PublicId == executionId);
    }
}
