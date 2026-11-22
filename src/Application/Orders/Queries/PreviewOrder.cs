using System.Text.Json;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Application.Orders.Commands;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

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

public class PreviewOrderQueryValidator : AbstractValidator<PreviewOrderQuery>
{
    public PreviewOrderQueryValidator()
    {
        RuleFor(query => query.AccountId).NotEmpty();
        RuleFor(query => query.InstrumentId).NotEmpty();
        RuleFor(query => query.Side).Must(OrderBodyRules.IsSide).WithMessage(OrderBodyRules.SideMessage);
        RuleFor(query => query.Quantity).Must(OrderBodyRules.IsQuantity).WithMessage(OrderBodyRules.QuantityMessage);
        RuleFor(query => query.Type)
            .Must(type => OrderBodyRules.IsOnly(type, nameof(OrderType.Market))).WithMessage("Type must be Market.");
        RuleFor(query => query.TimeInForce)
            .Must(timeInForce => OrderBodyRules.IsOnly(timeInForce, nameof(Domain.Enums.TimeInForce.Day)))
            .WithMessage("TimeInForce must be Day.");
        RuleFor(query => query.TenantId).Must(OrderBodyRules.IsAbsent).WithMessage("TenantId cannot be set.");
    }
}

public class PreviewOrderQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrencyCatalog currencies,
    IBrokerConnections connections,
    IMarketData marketData,
    IInstrumentPriceStore prices,
    TimeProvider time) : IRequestHandler<PreviewOrderQuery, OrderPreviewDto>
{
    public async Task<OrderPreviewDto> Handle(PreviewOrderQuery request, CancellationToken cancellationToken)
    {
        // R1, R7: the Account is in scope, or the preview is 404.
        var accountId = await AccountScope.ManagedAccounts(db, currentUser).AsNoTracking()
                            .Where(row => row.PublicId == request.AccountId)
                            .Select(row => (int?)row.Id)
                            .SingleOrDefaultAsync(cancellationToken)
                        ?? throw new NotFoundException("Account", request.AccountId!.Value);

        var quote = await OrderChecks.RunAsync(
            new OrderCheckServices(db, currencies, connections, marketData, prices),
            accountId, request.InstrumentId!.Value, OrderBodyRules.Side(request.Side!), request.Quantity!.Value,
            time.GetUtcNow(), cancellationToken);

        return new OrderPreviewDto(
            quote.Currency, quote.ReferencePrice, quote.ReferencePriceDate, quote.PriceLimit, quote.EstimatedGross,
            quote.EstimatedFee, quote.MaximumGross, quote.MaximumFee, quote.MaximumCost, quote.AvailableCash,
            quote.AvailableQuantity);
    }
}
