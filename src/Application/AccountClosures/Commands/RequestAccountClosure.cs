using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Interest.Accruals;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Application.Withdrawals;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.AccountClosures.Commands;

/// <summary>
/// Account closure R2–R10: request the closure of a Custodial account. With a payout it creates the withdrawal and
/// the Account is closing (R8); without one the remainder is written off and the Account closed at once (R9).
/// </summary>
[Authorize(Policy = Policies.AccountClosuresRequest)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RequestAccountClosureCommand : IRequest<IdempotentResult>
{
    /// <summary>The Account of the path (R2).</summary>
    [JsonIgnore]
    public Guid PathAccountId { get; set; }

    /// <summary>Refused when present: the Account is the path's (R2).</summary>
    public Guid? AccountId { get; init; }

    public Guid? NominatedBankAccountId { get; init; }

    public string? Memo { get; init; }

    /// <summary>Refused when present (R1).</summary>
    public Guid? TenantId { get; init; }

    [JsonIgnore]
    public string? IdempotencyKey { get; set; }

    [JsonIgnore]
    public string? RequestMethod { get; set; }

    [JsonIgnore]
    public string? RequestPath { get; set; }
}

public class RequestAccountClosureCommandValidator : AbstractValidator<RequestAccountClosureCommand>
{
    public RequestAccountClosureCommandValidator()
    {
        RuleFor(command => command.Memo)
            .Must(memo => memo is not null && memo.Trim().Length is >= 1 and <= AccountClosure.MemoMaxLength)
            .WithMessage($"Memo is required and must be 1 to {AccountClosure.MemoMaxLength} characters.");
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(command => command.AccountId).Null().WithMessage("AccountId cannot be set; the account is the path's.");
        RuleFor(command => command.IdempotencyKey)
            .Must(key => Guid.TryParse(key, out _))
            .WithMessage("Idempotency-Key is required and must be a UUID.");
    }
}

public class RequestAccountClosureCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrencyCatalog currencies,
    IAccountInterest accountInterest,
    TimeProvider time) : IRequestHandler<RequestAccountClosureCommand, IdempotentResult>
{
    public const string ExternalMessage = "Only a Custodial account is closed with an account closure.";
    public const string InProgressMessage = "This account is already being closed.";
    public const string LiveOrderMessage = "Open orders must be filled, cancelled or expired before the account can be closed.";
    public const string UnsettledFillMessage = "Trades must settle before the account can be closed.";
    public const string BrokenTradeMessage = "A broken trade on this account waits for a decision.";
    public const string OpenWithdrawalMessage =
        "Open withdrawals must be paid, rejected or cancelled before the account can be closed.";
    public const string AssignmentMessage = "Unassign the portfolio before closing the account.";
    public const string HoldingMessage = "Sell or transfer out every holding before closing the account.";

    /// <summary>Dividends R46: an account is not closed while it is owed a dividend.</summary>
    public const string PendingDividendMessage = "A dividend is still to be paid on this account. Close it after the payment.";

    /// <summary>In-kind-transfers R45: an open transfer of securities keeps the account open.</summary>
    public const string OpenTransferMessage =
        "Open transfers of securities must be completed, rejected, cancelled or set aside before the account can be closed.";

    /// <summary>Adjustment review R17: an open adjustment request says the balance may be wrong.</summary>
    public const string OpenAdjustmentRequestMessage =
        "Open adjustment requests must be approved, rejected or cancelled before the account can be closed.";

    public async Task<IdempotentResult> Handle(RequestAccountClosureCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var key = Guid.Parse(request.IdempotencyKey!);
        var method = request.RequestMethod ?? "POST";
        var path = request.RequestPath ?? $"/accounts/{request.PathAccountId}/closures";
        var hash = TransactionRequestHash.Compute(method, path, new
        {
            nominatedBankAccountId = request.NominatedBankAccountId,
            memo = request.Memo
        });

        IdempotentResult? result = null;
        try
        {
            await db.ExecuteInTransactionAsync(async token =>
            {
                result = await RequestAsync(request, tenantId, key, method, path, hash, token);
            }, cancellationToken);
        }
        catch (ValidationException exception) when (IsUniqueConstraint(exception))
        {
            db.ClearTrackedChanges();
            var existing = await FindRecordAsync(tenantId, key, cancellationToken);
            if (existing is not null)
            {
                return ReplayOrConflict(existing, hash);
            }

            throw;
        }

        return result!;
    }

    private async Task<IdempotentResult> RequestAsync(
        RequestAccountClosureCommand request,
        int tenantId,
        Guid key,
        string method,
        string path,
        string hash,
        CancellationToken cancellationToken)
    {
        var existing = await FindRecordAsync(tenantId, key, cancellationToken);
        if (existing is not null)
        {
            return ReplayOrConflict(existing, hash);
        }

        // R3: the Account of the path is in scope, or the whole command is 404.
        var accountId = await AccountClosureReader.AccountIdAsync(db, currentUser, request.PathAccountId, cancellationToken);

        // R4: R5–R7 are checked and R8 or R9 written under the Account lock, as every posting is.
        await db.LockAccountAsync(accountId, cancellationToken);
        var account = await db.Accounts.SingleAsync(row => row.Id == accountId, cancellationToken);
        var customer = await db.Users.AsNoTracking().SingleAsync(row => row.Id == account.CustomerId, cancellationToken);

        if (account.CustodyMode != CustodyMode.Custodial)
        {
            throw Error(nameof(RequestAccountClosureCommand.AccountId), ExternalMessage);
        }

        if (account.Status == AccountStatus.Closed)
        {
            throw Error(string.Empty, "Cannot post a transaction while the account is closed. Reopen the account first.");
        }

        if (customer.Status == UserStatus.Disabled)
        {
            throw new TargetDisabledException(
                "user",
                customer.PublicId,
                "User is disabled",
                "Cannot close the account while the customer is disabled. Enable the customer first.");
        }

        // R5: nothing in progress on the Account.
        if (await db.AccountClosures.AnyAsync(
                row => row.AccountId == account.Id && row.Status == AccountClosureStatus.InProgress, cancellationToken))
        {
            throw Error(nameof(RequestAccountClosureCommand.AccountId), InProgressMessage);
        }

        if (await db.Withdrawals.AnyAsync(
                row => row.AccountId == account.Id
                       && (row.Status == WithdrawalStatus.Requested
                           || row.Status == WithdrawalStatus.Approved
                           || row.Status == WithdrawalStatus.Sent),
                cancellationToken))
        {
            throw Error(nameof(RequestAccountClosureCommand.AccountId), OpenWithdrawalMessage);
        }

        if (await db.AccountPortfolioAssignments.AnyAsync(
                row => row.AccountId == account.Id && row.Status == AssignmentStatus.Active, cancellationToken))
        {
            throw Error(nameof(RequestAccountClosureCommand.AccountId), AssignmentMessage);
        }

        // In-kind-transfers R45: an open transfer in or out waits.
        if (await InKindTransfers.InKindTransferRules.HasOpenTransferAsync(db, account.Id, cancellationToken))
        {
            throw Error(nameof(RequestAccountClosureCommand.AccountId), OpenTransferMessage);
        }

        // Adjustment review R17: an open adjustment request waits for its decision.
        if (await db.AdjustmentRequests.AnyAsync(
                row => row.AccountId == account.Id && row.Status == AdjustmentRequestStatus.Requested,
                cancellationToken))
        {
            throw Error(nameof(RequestAccountClosureCommand.AccountId), OpenAdjustmentRequestMessage);
        }

        // Dividends R46: a Pending dividend entitlement keeps the account open until it is posted.
        if (await PendingDividends.AnyAsync(db, account.Id, cancellationToken))
        {
            throw Error(nameof(RequestAccountClosureCommand.AccountId), PendingDividendMessage);
        }

        // Orders R47: a live order (open, or with a fill not posted yet) and a fill that has not settled both wait.
        if (await Orders.OrderReservations.HasLiveOrderAsync(db, account.Id, cancellationToken))
        {
            throw Error(nameof(RequestAccountClosureCommand.AccountId), LiveOrderMessage);
        }

        // Trade-settlement R33: a break that could not be reversed waits for a TenantAdmin; a broken fill never settles.
        if (await db.OrderFills.AnyAsync(
                fill => fill.BreakOutcome == FillBreakOutcome.NotApplied
                        && db.Orders.Any(row => row.Id == fill.OrderId && row.AccountId == account.Id),
                cancellationToken))
        {
            throw Error(nameof(RequestAccountClosureCommand.AccountId), BrokenTradeMessage);
        }

        if (await db.OrderFills.AnyAsync(
                fill => fill.SettledAt == null && fill.Status != OrderFillStatus.Broken
                        && db.Orders.Any(row => row.Id == fill.OrderId && row.AccountId == account.Id),
                cancellationToken))
        {
            throw Error(nameof(RequestAccountClosureCommand.AccountId), UnsettledFillMessage);
        }

        // R6: the closure does not sell or move holdings.
        if (await db.Holdings.AnyAsync(row => row.AccountId == account.Id, cancellationToken))
        {
            throw Error(nameof(RequestAccountClosureCommand.AccountId), HoldingMessage);
        }

        var requester = currentUser.Person?.Id ?? throw new ForbiddenAccessException();
        var now = time.GetUtcNow();

        // Interest-run R33: under the same lock, the interest accrued through yesterday is posted first, whatever
        // its month; a posting is paid by a run with trigger AccountClosure for this account and the requester.
        var yesterday = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
        var interestRun = InterestRun.Start(
            tenantId, yesterday, InterestRunTrigger.AccountClosure, requester, account.Id, now);
        var interest = await accountInterest.AccrueAndPostAsync(
            account, yesterday, yesterday, interestRun.PublicId, cancellationToken);
        if (interest.Posting is not null)
        {
            interestRun.CreatedBy = SystemActor.Name;
            interestRun.Complete(1, 0, 1, now);
            db.InterestRuns.Add(interestRun);
            await db.SaveChangesAsync(cancellationToken);
        }

        // R7: no cash is reserved (R5), so the cash SUM is what is paid out and written off.
        var currency = account.Currency.Trim();
        var places = currencies.TryGet(currency)!.DecimalPlaces;
        var cash = await TransactionCash.SumAsync(db, account.Id, cancellationToken);
        var (payout, remainder) = ClosureAmounts.Split(cash, places);
        var closurePublicId = Guid.NewGuid();

        if (payout > 0m)
        {
            return await StartWithPayoutAsync(
                request, account, tenantId, requester, now, cash, payout, places, closurePublicId,
                (key, method, path, hash), cancellationToken);
        }

        // R9: nothing leaves the trust account; the remainder is written off and the Account closed at once.
        Transaction? writeOff = null;
        if (remainder > 0m)
        {
            writeOff = Transaction.PostRemainderWriteOff(
                tenantId, account.Id, currency, places, remainder, now, TransactionSource.AccountClosure(closurePublicId));
            db.Transactions.Add(writeOff);
            await db.SaveChangesAsync(cancellationToken);
        }

        account.Close();
        db.AccountClosures.Add(AccountClosure.CompleteAtOnce(
            tenantId, account.Id, currency, requester, now, request.Memo!, cash,
            writeOff is null ? null : remainder, writeOff?.Id, closurePublicId));
        return await SaveAsync(tenantId, key, method, path, hash, closurePublicId, now, cancellationToken);
    }

    /// <summary>R8: the payout is an ordinary withdrawal to a verified bank account of the Customer, created with the
    /// closure and requested by the same person at the same time; the Account is closing from now on.</summary>
    private async Task<IdempotentResult> StartWithPayoutAsync(
        RequestAccountClosureCommand request,
        Account account,
        int tenantId,
        int requester,
        DateTimeOffset now,
        decimal cash,
        decimal payout,
        int places,
        Guid closurePublicId,
        (Guid Key, string Method, string Path, string Hash) idempotency,
        CancellationToken cancellationToken)
    {
        var currency = account.Currency.Trim();
        if (request.NominatedBankAccountId is not Guid nominatedId)
        {
            throw Error(
                NominatedPayee.ErrorKey,
                $"The account has {payout.ToString($"F{places}", CultureInfo.InvariantCulture)} {currency} to pay out. Choose a verified bank account.");
        }

        var nominated = await NominatedPayee.FindAsync(db, tenantId, account.CustomerId, nominatedId, cancellationToken);
        NominatedPayee.EnsurePayable(nominated, currency);

        // Withdrawals R23: the reference is the deposit reference, so the Customer recognises the payment.
        var withdrawal = Withdrawal.Request(
            tenantId, account.Id, nominated.Id, payout, currency, account.DepositReference!.Value, request.Memo,
            requester, now);
        db.Withdrawals.Add(withdrawal);
        await db.SaveChangesAsync(cancellationToken);

        db.AccountClosures.Add(AccountClosure.StartWithPayout(
            tenantId, account.Id, currency, requester, now, request.Memo!, cash, payout, nominated.Id, withdrawal.Id,
            closurePublicId));
        return await SaveAsync(
            tenantId, idempotency.Key, idempotency.Method, idempotency.Path, idempotency.Hash, closurePublicId, now,
            cancellationToken);
    }

    private async Task<IdempotentResult> SaveAsync(
        int tenantId,
        Guid key,
        string method,
        string path,
        string hash,
        Guid closurePublicId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { id = closurePublicId });
        db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenantId, key, method, path, hash, 201, body, now));
        await db.SaveChangesAsync(cancellationToken);
        return new IdempotentResult(201, body);
    }

    private static IdempotentResult ReplayOrConflict(IdempotencyRecord existing, string hash) =>
        string.Equals(existing.RequestHash, hash, StringComparison.Ordinal)
            ? new IdempotentResult(existing.ResponseStatus, existing.ResponseBody)
            : throw new IdempotencyConflictException();

    private Task<IdempotencyRecord?> FindRecordAsync(int tenantId, Guid key, CancellationToken cancellationToken) =>
        db.IdempotencyRecords.SingleOrDefaultAsync(record => record.TenantId == tenantId && record.Key == key, cancellationToken);

    private static bool IsUniqueConstraint(ValidationException exception) =>
        exception.Errors.Values.Any(messages => messages.Contains("A unique constraint was violated."));

    private static ValidationException Error(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
