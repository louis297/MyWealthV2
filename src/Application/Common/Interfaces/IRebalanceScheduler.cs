namespace MyWealthV2.Application.Common.Interfaces;

public interface IRebalanceScheduler
{
    void Enqueue(Guid accountId, Guid handle);
}
