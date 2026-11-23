using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.TradeSettlements.Commands;

/// <summary>Trade-settlement R15: a TenantAdmin ends a settlement the firm and the broker resolve outside the
/// platform, with a note.</summary>
[Authorize(Policy = Policies.TradeSettlementsManage)]
public sealed class SetAsideTradeSettlementCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public JsonElement? TenantId { get; init; }
}

public class SetAsideTradeSettlementCommandValidator : AbstractValidator<SetAsideTradeSettlementCommand>
{
    public SetAsideTradeSettlementCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note)
            .Must(note => note is not null && note.Trim().Length is >= 1 and <= TradeSettlement.NoteMaxLength)
            .WithMessage(TradeSettlementRules.NoteMessage);
        RuleFor(command => command.TenantId).Must(TradeSettlementRules.IsAbsent).WithMessage("TenantId cannot be set.");
    }
}

public class SetAsideTradeSettlementCommandHandler : IRequestHandler<SetAsideTradeSettlementCommand>
{
    public Task Handle(SetAsideTradeSettlementCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
