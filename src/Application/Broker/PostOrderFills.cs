using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Orders;

namespace MyWealthV2.Application.Broker;

/// <summary>What the last step of a run did: fills posted, fills that still wait with a hold reason, and fills whose
/// posting failed.</summary>
public sealed record FillPostingOutcome(int Posted, int Waiting, int Failed);

/// <summary>
/// The one place a fill is posted (docs/features/orders.md R31–R36).
/// </summary>
public sealed class PostOrderFills
{
    /// <summary>R36: tries every <c>Pending</c> fill of the connection again, oldest first, each in its own database
    /// transaction.</summary>
    public Task<FillPostingOutcome> RunAsync(string connection, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>R31–R36: posts one <c>Pending</c> fill of <paramref name="order"/>, or holds it with the reason it
    /// cannot be posted. True when the fill is posted.</summary>
    public Task<bool> PostAsync(Order order, OrderFill fill, OrderFeeTerms fees, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
