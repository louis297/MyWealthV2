using System.Globalization;
using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Withdrawals.Commands;

/// <summary>Withdrawals R18–R23: request a withdrawal and reserve its amount.</summary>
[Authorize(Policy = Policies.WithdrawalsRequest)]
public sealed class RequestWithdrawalCommand : IRequest<IdempotentResult>
{
    public Guid? AccountId { get; init; }

    public Guid? NominatedBankAccountId { get; init; }

    public decimal? Amount { get; init; }

    public string? Reference { get; init; }

    public string? Memo { get; init; }

    /// <summary>Refused when present (R1).</summary>
    public Guid? TenantId { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }
}

public class RequestWithdrawalCommandValidator : AbstractValidator<RequestWithdrawalCommand>
{
    public RequestWithdrawalCommandValidator()
    {
        RuleFor(command => command.AccountId).NotEmpty();
        RuleFor(command => command.NominatedBankAccountId).NotEmpty();
        RuleFor(command => command.Amount).NotNull().GreaterThan(0).WithMessage("Amount must be greater than 0.");
        RuleFor(command => command.Reference)
            .Must(reference => reference is null || Withdrawal.IsValidReference(reference))
            .WithMessage($"Reference must be 1 to {Withdrawal.ReferenceMaxLength} letters, digits, spaces, hyphens and full stops.");
        RuleFor(command => command.Memo)
            .Must(memo => memo is null || memo.Trim().Length is >= 1 and <= Withdrawal.MemoMaxLength)
            .WithMessage($"Memo must be 1 to {Withdrawal.MemoMaxLength} characters.");
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(command => command.IdempotencyKey)
            .Must(key => Guid.TryParse(key, out _))
            .WithMessage("Idempotency-Key is required and must be a UUID.");
    }
}

public class RequestWithdrawalCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrencyCatalog currencies,
    TimeProvider time) : IRequestHandler<RequestWithdrawalCommand, IdempotentResult>
{
    private const string DefaultPath = "/withdrawals";

    public async Task<IdempotentResult> Handle(RequestWithdrawalCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        var key = Guid.Parse(request.IdempotencyKey!);
        var method = request.RequestMethod ?? "POST";
        var path = request.RequestPath ?? DefaultPath;
        var hash = TransactionRequestHash.Compute(method, path, new
        {
            accountId = request.AccountId,
            nominatedBankAccountId = request.NominatedBankAccountId,
            amount = request.Amount,
            reference = request.Reference,
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
        RequestWithdrawalCommand request,
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

        // R19: the Account is in scope, or the whole command is 404.
        var accountId = await AccountScope.ManagedAccounts(db, currentUser).AsNoTracking()
                            .Where(row => row.PublicId == request.AccountId)
                            .Select(row => (int?)row.Id)
                            .SingleOrDefaultAsync(cancellationToken)
                        ?? throw new NotFoundException("Account", request.AccountId!.Value);

        // R22: the reservation is checked under the Account lock, as every posting is.
        await db.LockAccountAsync(accountId, cancellationToken);
        var account = await db.Accounts.AsNoTracking().SingleAsync(row => row.Id == accountId, cancellationToken);
        var customer = await db.Users.AsNoTracking().SingleAsync(row => row.Id == account.CustomerId, cancellationToken);

        // R20: another Customer's, another tenant's or a missing nominated bank account is 404 for the whole command.
        var nominated = await db.NominatedBankAccounts.AsNoTracking()
                            .SingleOrDefaultAsync(row => row.PublicId == request.NominatedBankAccountId
                                                         && row.TenantId == tenantId
                                                         && row.CustomerId == account.CustomerId, cancellationToken)
                        ?? throw new NotFoundException("Nominated bank account", request.NominatedBankAccountId!.Value);

        if (account.CustodyMode != CustodyMode.Custodial)
        {
            throw Error(nameof(RequestWithdrawalCommand.AccountId), "A withdrawal can be requested only from a Custodial account.");
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
                "Cannot request a withdrawal while the customer is disabled. Enable the customer first.");
        }

        var currency = account.Currency.Trim();
        if (nominated.Status != NominatedBankAccountStatus.Verified)
        {
            throw Error(nameof(RequestWithdrawalCommand.NominatedBankAccountId), "The bank account is not verified.");
        }

        if (!string.Equals(nominated.Currency.Trim(), currency, StringComparison.OrdinalIgnoreCase))
        {
            throw Error(
                nameof(RequestWithdrawalCommand.NominatedBankAccountId),
                $"The bank account is in {nominated.Currency.Trim()}; this account is {currency}.");
        }

        // R21: precision, then the available cash under the lock.
        var amount = request.Amount!.Value;
        CashAmountPlaces.EnsureFits(currencies, currency, amount);
        var available = await AccountCash.AvailableAsync(db, account.Id, cancellationToken);
        if (amount > available)
        {
            var places = currencies.TryGet(currency)!.DecimalPlaces;
            throw Error(
                nameof(RequestWithdrawalCommand.Amount),
                $"The account's available cash is {available.ToString($"F{places}", CultureInfo.InvariantCulture)} {currency}.");
        }

        // R23: the reference defaults to the deposit reference, so the Customer recognises the payment.
        var reference = request.Reference?.Trim() ?? account.DepositReference!.Value;
        var withdrawal = Withdrawal.Request(
            tenantId, account.Id, nominated.Id, amount, currency, reference, request.Memo,
            currentUser.Person?.Id ?? throw new ForbiddenAccessException(), time.GetUtcNow());
        db.Withdrawals.Add(withdrawal);
        var body = JsonSerializer.Serialize(new { id = withdrawal.PublicId });
        db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenantId, key, method, path, hash, 201, body, time.GetUtcNow()));
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
