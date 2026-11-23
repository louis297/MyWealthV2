using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.TradeSettlements.Commands;

/// <summary>Trade-settlement R14: a TenantAdmin pays a failed settlement again, with a new instruction id.</summary>
[Authorize(Policy = Policies.TradeSettlementsManage)]
public sealed class PayTradeSettlementCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public JsonElement? TenantId { get; init; }
}

public class PayTradeSettlementCommandHandler : IRequestHandler<PayTradeSettlementCommand>
{
    public Task Handle(PayTradeSettlementCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
