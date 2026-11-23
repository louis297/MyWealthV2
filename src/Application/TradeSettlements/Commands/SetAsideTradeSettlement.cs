using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;

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

public class SetAsideTradeSettlementCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<SetAsideTradeSettlementCommand>
{
    public async Task Handle(SetAsideTradeSettlementCommand request, CancellationToken cancellationToken)
    {
        var settlement = await TradeSettlementRules.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken);
        var actor = currentUser.Person?.Id ?? throw new ForbiddenAccessException();
        var open = await db.OutgoingPayments
            .Where(row => row.TradeSettlementId == settlement.Id
                          && (row.Status == OutgoingPaymentStatus.Pending || row.Status == OutgoingPaymentStatus.Accepted))
            .SingleOrDefaultAsync(cancellationToken);
        try
        {
            settlement.SetAside(actor, request.Note!, time.GetUtcNow(), paymentAttempted: open is { Attempts: > 0 } or { Status: OutgoingPaymentStatus.Accepted });
        }
        catch (TradeSettlementStatusException exception)
        {
            throw TradeSettlementRules.StatusError(exception.Message);
        }

        // R15: the payment the bank was never asked for is cancelled with it.
        open?.Cancel();
        await TradeSettlementRules.SaveAsync(db, cancellationToken);
    }
}
