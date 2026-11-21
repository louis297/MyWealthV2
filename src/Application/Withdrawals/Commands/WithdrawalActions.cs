using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Withdrawals.Commands;

/// <summary>Withdrawals R27–R29, R31: a TenantAdmin other than the requester approves; an outgoing payment waits.</summary>
[Authorize(Policy = Policies.WithdrawalsApprove)]
public sealed class ApproveWithdrawalCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>Withdrawals R27, R28: a TenantAdmin other than the requester refuses it, with a note.</summary>
[Authorize(Policy = Policies.WithdrawalsApprove)]
public sealed class RejectWithdrawalCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>Withdrawals R27, R30: withdrawn before the bank was asked to pay it.</summary>
[Authorize(Policy = Policies.WithdrawalsRequest)]
public sealed class CancelWithdrawalCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

internal static class WithdrawalActionRules
{
    public const string NoteMessage = "Note must be 1 to 200 characters.";

    public static bool IsOptionalNote(string? note) => (note?.Trim().Length ?? 0) <= Withdrawal.NoteMaxLength;

    public static bool IsRequiredNote(string? note) => note?.Trim().Length is >= 1 and <= Withdrawal.NoteMaxLength;
}

public class ApproveWithdrawalCommandValidator : AbstractValidator<ApproveWithdrawalCommand>
{
    public ApproveWithdrawalCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(WithdrawalActionRules.IsOptionalNote).WithMessage(WithdrawalActionRules.NoteMessage);
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class RejectWithdrawalCommandValidator : AbstractValidator<RejectWithdrawalCommand>
{
    public RejectWithdrawalCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(WithdrawalActionRules.IsRequiredNote).WithMessage(WithdrawalActionRules.NoteMessage);
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class CancelWithdrawalCommandValidator : AbstractValidator<CancelWithdrawalCommand>
{
    public CancelWithdrawalCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(WithdrawalActionRules.IsOptionalNote).WithMessage(WithdrawalActionRules.NoteMessage);
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class ApproveWithdrawalCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<ApproveWithdrawalCommand>
{
    public async Task Handle(ApproveWithdrawalCommand request, CancellationToken cancellationToken)
    {
        var withdrawal = await WithdrawalChange.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken);
        var actor = WithdrawalChange.ActorId(currentUser);
        await db.ExecuteInTransactionAsync(async token =>
        {
            // R29: the checks run under the Account lock, as the request's did.
            await db.LockAccountAsync(withdrawal.AccountId, token);
            WithdrawalChange.Guard(() => withdrawal.Approve(actor, request.Note, time.GetUtcNow()));
            var account = await db.Accounts.AsNoTracking().SingleAsync(row => row.Id == withdrawal.AccountId, token);
            if (account.Status == AccountStatus.Closed)
            {
                throw WithdrawalChange.Error(string.Empty, "Cannot post a transaction while the account is closed. Reopen the account first.");
            }

            var customer = await db.Users.AsNoTracking().SingleAsync(row => row.Id == account.CustomerId, token);
            if (customer.Status == UserStatus.Disabled)
            {
                throw new TargetDisabledException(
                    "user",
                    customer.PublicId,
                    "User is disabled",
                    "Cannot approve a withdrawal while the customer is disabled. Enable the customer first.");
            }

            var nominated = await db.NominatedBankAccounts.AsNoTracking()
                .SingleAsync(row => row.Id == withdrawal.NominatedBankAccountId, token);
            if (nominated.Status != NominatedBankAccountStatus.Verified)
            {
                throw WithdrawalChange.Error("NominatedBankAccountId", "The bank account is no longer verified.");
            }

            var clientMoney = await db.ClientMoneyAccounts.AsNoTracking()
                                  .SingleOrDefaultAsync(row => row.TenantId == withdrawal.TenantId
                                                               && row.Currency == withdrawal.Currency
                                                               && row.IsActive, token)
                              ?? throw WithdrawalChange.Error(
                                  "AccountId", $"There is no active client money account in {withdrawal.Currency} to pay from.");

            db.OutgoingPayments.Add(OutgoingPayment.ForWithdrawal(withdrawal, clientMoney));
            await WithdrawalChange.SaveAsync(db, token);
        }, cancellationToken);
    }
}

public class RejectWithdrawalCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<RejectWithdrawalCommand>
{
    public async Task Handle(RejectWithdrawalCommand request, CancellationToken cancellationToken)
    {
        var withdrawal = await WithdrawalChange.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken);
        var actor = WithdrawalChange.ActorId(currentUser);
        WithdrawalChange.Guard(() => withdrawal.Reject(actor, request.Note!, time.GetUtcNow()));
        await WithdrawalChange.SaveAsync(db, cancellationToken);
    }
}

public class CancelWithdrawalCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<CancelWithdrawalCommand>
{
    public async Task Handle(CancelWithdrawalCommand request, CancellationToken cancellationToken)
    {
        var withdrawal = await WithdrawalChange.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken);
        var actor = WithdrawalChange.ActorId(currentUser);
        var approved = withdrawal.Status == WithdrawalStatus.Approved;
        WithdrawalChange.Guard(() => withdrawal.Cancel(actor, request.Note, time.GetUtcNow()));
        if (approved)
        {
            // R30: only before the first send attempt; the payment's row version guards the race with the send step.
            var payment = await db.OutgoingPayments.SingleAsync(row => row.WithdrawalId == withdrawal.Id, cancellationToken);
            WithdrawalChange.Guard(payment.Cancel);
        }

        await WithdrawalChange.SaveAsync(db, cancellationToken);
    }
}

/// <summary>What every withdrawal action shares (R27).</summary>
internal static class WithdrawalChange
{
    /// <summary>The withdrawal in scope, tracked, with a current <c>rowVersion</c>: else 404 or 409.</summary>
    public static async Task<Withdrawal> FindAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid id,
        string? rowVersion,
        CancellationToken cancellationToken)
    {
        var withdrawal = await WithdrawalReader.Scope(db, currentUser)
                             .SingleOrDefaultAsync(row => row.PublicId == id, cancellationToken)
                         ?? throw new NotFoundException("Withdrawal", id);
        if (!string.Equals(Convert.ToBase64String(withdrawal.RowVersion ?? []), rowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        return withdrawal;
    }

    public static int ActorId(ICurrentUser currentUser) =>
        currentUser.Person?.Id ?? throw new ForbiddenAccessException();

    /// <summary>R27, R28, R30: a wrong status, the requester approving, or a payment already attempted is 400 at
    /// <c>Status</c>.</summary>
    public static void Guard(Action change)
    {
        try
        {
            change();
        }
        catch (Exception exception) when (exception is WithdrawalStatusException or OutgoingPaymentAttemptedException)
        {
            throw Error("Status", exception.Message);
        }
    }

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

    public static ValidationException Error(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
