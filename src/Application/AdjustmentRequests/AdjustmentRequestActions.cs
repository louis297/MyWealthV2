using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.AdjustmentRequests;

/// <summary>Adjustment review R7: the body of every decision on an adjustment request.</summary>
public abstract class AdjustmentRequestActionCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>R8, R9: a TenantAdmin other than the requester approves the request, and the adjustment is posted.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public sealed class ApproveAdjustmentRequestCommand : AdjustmentRequestActionCommand;

/// <summary>R8, R10: a TenantAdmin other than the requester refuses the request, with a note.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public sealed class RejectAdjustmentRequestCommand : AdjustmentRequestActionCommand;

/// <summary>R11: any TenantAdmin withdraws the request, the requester included.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public sealed class CancelAdjustmentRequestCommand : AdjustmentRequestActionCommand;

internal static class AdjustmentRequestActionRules
{
    public const string NoteMessage = "Note must be 1 to 200 characters.";

    public static bool IsOptionalNote(string? note) => (note?.Trim().Length ?? 0) <= AdjustmentRequest.NoteMaxLength;

    public static bool IsRequiredNote(string? note) => note?.Trim().Length is >= 1 and <= AdjustmentRequest.NoteMaxLength;

    public static void Rules<T>(AbstractValidator<T> validator, bool noteRequired)
        where T : AdjustmentRequestActionCommand
    {
        validator.RuleFor(command => command.RowVersion).NotEmpty();
        validator.RuleFor(command => command.Note)
            .Must(noteRequired ? IsRequiredNote : IsOptionalNote)
            .WithMessage(NoteMessage);
        validator.RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class ApproveAdjustmentRequestCommandValidator : AbstractValidator<ApproveAdjustmentRequestCommand>
{
    public ApproveAdjustmentRequestCommandValidator() => AdjustmentRequestActionRules.Rules(this, noteRequired: false);
}

public class ApproveAdjustmentRequestCommandHandler(
    IApplicationDbContext db, ICurrentUser currentUser, ICurrencyCatalog catalog, TimeProvider time)
    : IRequestHandler<ApproveAdjustmentRequestCommand>
{
    public async Task Handle(ApproveAdjustmentRequestCommand request, CancellationToken cancellationToken)
    {
        var adjustmentRequest = await AdjustmentRequestChange.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken);
        var actor = AdjustmentRequestChange.ActorId(currentUser);
        await db.ExecuteInTransactionAsync(async token =>
        {
            // R9: the Account is locked, then the clock is read (custody-modes R12).
            await db.LockAccountAsync(adjustmentRequest.AccountId, token);
            var now = time.GetUtcNow();

            // R7, R8: the status and the second person come before the Account.
            if (adjustmentRequest.Status != AdjustmentRequestStatus.Requested)
            {
                throw new ValidationException([
                    new ValidationFailure("Status", AdjustmentRequest.StatusMessage(adjustmentRequest.Status, "approved"))
                ]);
            }

            if (actor == adjustmentRequest.RequestedByUserId)
            {
                throw new ValidationException([new ValidationFailure("Status", AdjustmentRequest.FourEyesMessage)]);
            }

            // R9: the checks of the request again, on the locked state, except the request shape.
            var tenant = await AdjustmentRequestChange.TenantAsync(db, currentUser, token);
            AdjustmentPosting.EnsureTenantActive(tenant);
            var row = await (
                    from account in db.Accounts
                    join customer in db.Users on account.CustomerId equals customer.Id
                    where account.Id == adjustmentRequest.AccountId
                    select new { account, customer })
                .SingleAsync(token);
            await AdjustmentPosting.EnsureAccountAcceptsAsync(db, row.account, row.customer, token);

            AdjustmentHoldingChange? holdingChange = null;
            if (adjustmentRequest.InstrumentId is int instrumentId)
            {
                var instrument = await db.Instruments.SingleAsync(item => item.Id == instrumentId, token);
                holdingChange = await AdjustmentPosting.HoldingChangeAsync(
                    db, instrument, row.account.Id, adjustmentRequest.QuantityChange!.Value, adjustmentRequest.CostChange!.Value, token);
            }

            var transaction = await AdjustmentPosting.BuildAsync(
                db,
                catalog,
                tenant.Id,
                row.account,
                adjustmentRequest.Reason,
                adjustmentRequest.Memo,
                adjustmentRequest.Reference,
                adjustmentRequest.CashAmount,
                holdingChange,
                now,
                TransactionSource.AdjustmentRequest(adjustmentRequest.PublicId),
                token);

            // Adjustments R16: no portfolio trade lock and no rebalance; the next rebalance sees the adjusted holding.
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync(token);
            if (transaction.SecurityLegs.Count > 0)
            {
                await HoldingProjection.SyncAsync(
                    db, tenant.Id, row.account.Id, transaction.SecurityLegs.Select(leg => leg.InstrumentId), token);
                await db.SaveChangesAsync(token);
            }

            var sumAfter = await TransactionCash.SumAsync(db, row.account.Id, token);
            await AdjustmentPosting.EnsureCashAfterAsync(db, catalog, row.account, sumAfter, token);

            AdjustmentRequestChange.Guard(() => adjustmentRequest.Approve(actor, now, request.Note, transaction.Id));
            await AdjustmentRequestChange.SaveAsync(db, token);
        }, cancellationToken);
    }
}

public class RejectAdjustmentRequestCommandHandler : IRequestHandler<RejectAdjustmentRequestCommand>
{
    public Task Handle(RejectAdjustmentRequestCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class CancelAdjustmentRequestCommandHandler : IRequestHandler<CancelAdjustmentRequestCommand>
{
    public Task Handle(CancelAdjustmentRequestCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
