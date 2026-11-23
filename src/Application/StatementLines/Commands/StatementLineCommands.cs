using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.StatementLines.Commands;

/// <summary>Deposit matching R26: match an unmatched receipt to a Custodial account by hand.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed class MatchStatementLineCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public Guid? AccountId { get; init; }

    public const string TargetMessage = "Give an account or a migration batch.";

    /// <summary>Migration-batches R13: the batch this receipt funds, in place of an account.</summary>
    public Guid? MigrationBatchId { get; init; }

    public string? RowVersion { get; init; }

    public string? Note { get; init; }

    /// <summary>Refused when present (R21).</summary>
    public Guid? TenantId { get; init; }
}

/// <summary>Deposit matching R27: money that is not a Customer's.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed class IgnoreStatementLineCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>Deposit matching R28: money that must go back to the payer.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed class RequestStatementLineReturnCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>Deposit matching R29: back into suspense.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed class ReopenStatementLineCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    /// <summary>Refused when present: reopen takes no note (R25).</summary>
    public string? Note { get; init; }

    public Guid? TenantId { get; init; }
}

public class MatchStatementLineCommandValidator : AbstractValidator<MatchStatementLineCommand>
{
    public MatchStatementLineCommandValidator()
    {
        // Migration-batches R13: exactly one of an account and a migration batch.
        RuleFor(command => command.AccountId)
            .Must((command, accountId) => IsGiven(accountId) != IsGiven(command.MigrationBatchId))
            .WithMessage(MatchStatementLineCommand.TargetMessage);
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(StatementLineActions.IsOptionalNote).WithMessage(StatementLineActions.NoteMessage);
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }

    private static bool IsGiven(Guid? id) => id is not null && id != Guid.Empty;
}

public class IgnoreStatementLineCommandValidator : AbstractValidator<IgnoreStatementLineCommand>
{
    public IgnoreStatementLineCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(StatementLineActions.IsRequiredNote).WithMessage(StatementLineActions.NoteMessage);
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class RequestStatementLineReturnCommandValidator : AbstractValidator<RequestStatementLineReturnCommand>
{
    public RequestStatementLineReturnCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(StatementLineActions.IsRequiredNote).WithMessage(StatementLineActions.NoteMessage);
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class ReopenStatementLineCommandValidator : AbstractValidator<ReopenStatementLineCommand>
{
    public ReopenStatementLineCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Null().WithMessage("Note cannot be set on reopen.");
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class MatchStatementLineCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrencyCatalog catalog,
    TimeProvider time) : IRequestHandler<MatchStatementLineCommand>
{
    public async Task Handle(MatchStatementLineCommand request, CancellationToken cancellationToken)
    {
        if (request.MigrationBatchId is Guid migrationBatchId)
        {
            await MigrationBatchFunding.MatchAsync(db, currentUser, time, request.Id, migrationBatchId, request.RowVersion,
                request.Note, cancellationToken);
            return;
        }

        var line = await StatementLineReader.FindAsync(db, currentUser, request.Id, cancellationToken);
        StatementLineActions.CheckRowVersion(line, request.RowVersion);
        StatementLineActions.Guard(line.EnsureCanMatchByHand);
        var actor = StatementLineActions.ActorId(currentUser);

        var account = await AccountScope.ManagedAccounts(db, currentUser).AsNoTracking()
                          .SingleOrDefaultAsync(row => row.PublicId == request.AccountId, cancellationToken)
                      ?? throw new NotFoundException("Account", request.AccountId!.Value);
        if (account.CustodyMode != CustodyMode.Custodial)
        {
            throw AccountIdError("A receipt can be matched only to a Custodial account.");
        }

        if (!string.Equals(account.Currency.Trim(), line.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw AccountIdError($"The account's currency is {account.Currency.Trim()}; this receipt is {line.Currency}.");
        }

        await db.ExecuteInTransactionAsync(async token =>
        {
            await db.LockAccountAsync(account.Id, token);
            var locked = await db.Accounts.AsNoTracking().SingleAsync(row => row.Id == account.Id, token);
            await StatementLinePosting.EnsureCanPostAsync(db, locked, token);
            if (!BankDeposits.AmountFits(catalog, line))
            {
                throw AccountIdError($"This receipt has more decimal places than {line.Currency} allows.");
            }

            var now = time.GetUtcNow();
            var deposit = await BankDeposits.PostAsync(db, line, locked, now, createdBy: null, token);
            line.MatchByHand(locked.Id, deposit.Id, request.Note, actor, now);
            await StatementLineActions.SaveAsync(db, token);
        }, cancellationToken);
    }

    private static ValidationException AccountIdError(string message) =>
        new([new ValidationFailure(nameof(MatchStatementLineCommand.AccountId), message)]);
}

public class IgnoreStatementLineCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<IgnoreStatementLineCommand>
{
    public Task Handle(IgnoreStatementLineCommand request, CancellationToken cancellationToken) =>
        StatementLineActions.ChangeAsync(db, currentUser, time, request.Id, request.RowVersion,
            (line, actor, now) => line.Ignore(request.Note!, actor, now), cancellationToken);
}

public class RequestStatementLineReturnCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<RequestStatementLineReturnCommand>
{
    /// <summary>Bank-returns R5: the decision and the return payment are saved together.</summary>
    public async Task Handle(RequestStatementLineReturnCommand request, CancellationToken cancellationToken)
    {
        var line = await StatementLineReader.FindAsync(db, currentUser, request.Id, cancellationToken);
        StatementLineActions.CheckRowVersion(line, request.RowVersion);
        var actor = StatementLineActions.ActorId(currentUser);
        StatementLineActions.Guard(() => line.RequestReturn(request.Note!, actor, time.GetUtcNow()));
        var clientMoney = await db.ClientMoneyAccounts.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == line.ClientMoneyAccountId, cancellationToken);
        var payment = OutgoingPayment.ForReceiptReturn(line, clientMoney);
        db.OutgoingPayments.Add(payment);
        await StatementLineActions.SaveAsync(db, cancellationToken);
    }
}

public class ReopenStatementLineCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<ReopenStatementLineCommand>
{
    /// <summary>Bank-returns R5: a line to be returned is reopened only while its return payment has no attempt;
    /// the payment is cancelled in the same save, and its row version guards the race with the send step.</summary>
    public async Task Handle(ReopenStatementLineCommand request, CancellationToken cancellationToken)
    {
        var line = await StatementLineReader.FindAsync(db, currentUser, request.Id, cancellationToken);
        StatementLineActions.CheckRowVersion(line, request.RowVersion);
        var actor = StatementLineActions.ActorId(currentUser);
        var payment = line.Status == BankStatementLineStatus.ReturnRequested
            ? await StatementLineReturns.OpenPaymentAsync(db, line.Id, cancellationToken)
            : null;
        var attempted = payment is not null && (payment.Attempts > 0 || payment.Status != OutgoingPaymentStatus.Pending);
        StatementLineActions.Guard(() => line.Reopen(actor, time.GetUtcNow(), attempted));
        payment?.Cancel();
        await StatementLineActions.SaveAsync(db, cancellationToken);
    }
}
