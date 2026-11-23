using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

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

public class PayTradeSettlementCommandValidator : AbstractValidator<PayTradeSettlementCommand>
{
    public PayTradeSettlementCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.TenantId).Must(TradeSettlementRules.IsAbsent).WithMessage("TenantId cannot be set.");
    }
}

public class PayTradeSettlementCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IBrokerConnections brokers)
    : IRequestHandler<PayTradeSettlementCommand>
{
    public async Task Handle(PayTradeSettlementCommand request, CancellationToken cancellationToken)
    {
        var settlement = await TradeSettlementRules.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken);
        if (settlement.Status != TradeSettlementStatus.PaymentFailed)
        {
            throw TradeSettlementRules.StatusError($"This settlement is {settlement.Status} and cannot be paid again.");
        }

        var currency = settlement.Currency.Trim();
        if (brokers.BankAccount(settlement.Connection, currency) is null)
        {
            throw TradeSettlementRules.StatusError($"The broker connection has no bank account in {currency}.");
        }

        settlement.PayAgain();
        var clientMoney = await db.ClientMoneyAccounts.AsNoTracking()
            .SingleAsync(row => row.Id == settlement.ClientMoneyAccountId, cancellationToken);
        db.OutgoingPayments.Add(OutgoingPayment.ForTradeSettlement(settlement, clientMoney));
        await TradeSettlementRules.SaveAsync(db, cancellationToken);
    }
}

/// <summary>What the trade settlement commands share: the lookup in the caller's Tenant (R1), the row version
/// (R16) and the 400 at <c>errors.Status</c>.</summary>
internal static class TradeSettlementRules
{
    public const string NoteMessage = "Note must be 1 to 200 characters.";

    public static bool IsAbsent(JsonElement? value) => value is null || value.Value.ValueKind == JsonValueKind.Null;

    public static async Task<TradeSettlement> FindAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid id,
        string? rowVersion,
        CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var settlement = await db.TradeSettlements
                             .SingleOrDefaultAsync(row => row.PublicId == id && row.TenantId == tenantId, cancellationToken)
                         ?? throw new NotFoundException("TradeSettlement", id);
        if (!string.Equals(Convert.ToBase64String(settlement.RowVersion ?? []), rowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        return settlement;
    }

    public static ValidationException StatusError(string message) =>
        new([new ValidationFailure("Status", message)]);

    public static async Task SaveAsync(IApplicationDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }
}
