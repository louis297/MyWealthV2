using System.Text.Json;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;

namespace MyWealthV2.Application.Orders.Commands;

/// <summary>Orders R6–R13: place an order and reserve the most it can cost (a buy) or its quantity (a sell).</summary>
[Authorize(Policy = Policies.OrdersPlace)]
public sealed class PlaceOrderCommand : IRequest<IdempotentResult>
{
    public Guid? AccountId { get; init; }

    public Guid? InstrumentId { get; init; }

    public string? Side { get; init; }

    public decimal? Quantity { get; init; }

    public string? Memo { get; init; }

    /// <summary>May be sent, and must then be <c>Market</c> (R6).</summary>
    public string? Type { get; init; }

    /// <summary>May be sent, and must then be <c>Day</c> (R6).</summary>
    public string? TimeInForce { get; init; }

    /// <summary>Refused when present (R6): the platform sets the price limit.</summary>
    public JsonElement? Price { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public JsonElement? PriceLimit { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public JsonElement? Limit { get; init; }

    /// <summary>Refused when present (R6): an order is by quantity.</summary>
    public JsonElement? Amount { get; init; }

    /// <summary>Refused when present (R6): a fill is booked when its report is processed.</summary>
    public JsonElement? BookedAt { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public JsonElement? Status { get; init; }

    /// <summary>Refused when present (R1).</summary>
    public JsonElement? TenantId { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public JsonElement? Id { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }
}

public class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, IdempotentResult>
{
    public Task<IdempotentResult> Handle(PlaceOrderCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
