using Hangfire;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Portfolios;

namespace MyWealthV2.Infrastructure.Reports;

public sealed class HangfireRebalanceScheduler(IBackgroundJobClient jobs) : IRebalanceScheduler
{
    public void Enqueue(Guid accountId, Guid handle)
    {
        jobs.Enqueue<RebalanceJob>("rebalance", job => job.Execute(accountId, handle));
    }
}
