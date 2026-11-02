using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.FunctionalTests.Portfolios;

public sealed class RecordingRebalanceScheduler : IRebalanceScheduler
{
    public List<(Guid AccountId, Guid Handle)> Enqueued { get; } = [];

    public void Enqueue(Guid accountId, Guid handle) => Enqueued.Add((accountId, handle));
}
