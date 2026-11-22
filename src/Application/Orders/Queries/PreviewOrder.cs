using System.Text.Json;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Orders.Queries;

/// <summary>
/// Orders R17: what an order would cost and reserve, with the 400s of R7–R13. It writes nothing and holds no price:
/// the order placed after it reads the price again.
/// </summary>
[Authorize(Policy = Policies.OrdersPlace)]
public sealed class PreviewOrderQuery : IRequest<OrderPreviewDto>
{
    public Guid? AccountId { get; init; }

    public Guid? InstrumentId { get; init; }

    public string? Side { get; init; }

    public decimal? Quantity { get; init; }

    /// <summary>May be sent, and must then be <c>Market</c> (R6).</summary>
    public string? Type { get; init; }

    /// <summary>May be sent, and must then be <c>Day</c> (R6).</summary>
    public string? TimeInForce { get; init; }

    /// <summary>Refused when present (R1).</summary>
    public JsonElement? TenantId { get; init; }
}

/// <summary>The preview of an order (R17). <see cref="MaximumCost"/> is what a buy would reserve, null for a sell;
/// <see cref="AvailableQuantity"/> is null for a buy.</summary>
public sealed record OrderPreviewDto(
    string Currency,
    decimal ReferencePrice,
    DateOnly ReferencePriceDate,
    decimal PriceLimit,
    decimal EstimatedGross,
    decimal EstimatedFee,
    decimal MaximumGross,
    decimal MaximumFee,
    decimal? MaximumCost,
    decimal AvailableCash,
    decimal? AvailableQuantity);

public class PreviewOrderQueryHandler : IRequestHandler<PreviewOrderQuery, OrderPreviewDto>
{
    public Task<OrderPreviewDto> Handle(PreviewOrderQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
