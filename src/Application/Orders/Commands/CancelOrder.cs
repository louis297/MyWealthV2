using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Orders.Commands;

/// <summary>
/// Orders R18: cancel an order. An order the broker cannot hold yet is <c>Cancelled</c> in the request; any other open
/// order gets a cancel request that the worker takes to the broker.
/// </summary>
[Authorize(Policy = Policies.OrdersPlace)]
public sealed class CancelOrderCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    /// <summary>Refused when present (R1).</summary>
    public JsonElement? TenantId { get; init; }
}

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand>
{
    public Task Handle(CancelOrderCommand request, CancellationToken cancellationToken) => throw new NotImplementedException();
}
