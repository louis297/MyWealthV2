using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.AdjustmentRequests;

/// <summary>Adjustment review R3: a TenantAdmin asks for a correction of the cash, one holding, or both, on one
/// Custodial account. The body is the body of an adjustment (adjustments §8.1).</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public class RequestAdjustmentCommand : IRequest<IdempotentResult>
{
    public Guid? AccountId { get; init; }

    public string? Reason { get; init; }

    public string? Memo { get; init; }

    public string? Reference { get; init; }

    public decimal? Amount { get; init; }

    public AdjustmentSecurity? Security { get; init; }

    /// <summary>Not part of the body (R1).</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Not part of the body: the approval books the adjustment at its own clock (R9).</summary>
    public DateTimeOffset? BookedAt { get; init; }

    /// <summary>Not part of the body: the route names the type.</summary>
    public string? Type { get; init; }

    /// <summary>Not part of the body: the Account currency is used.</summary>
    public string? Currency { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }

    /// <summary>The holding part: one instrument and the signed changes.</summary>
    public sealed class AdjustmentSecurity
    {
        public Guid InstrumentId { get; init; }

        public decimal QuantityChange { get; init; }

        public decimal CostChange { get; init; }

        /// <summary>Not part of the body: the instrument's quote currency is used.</summary>
        public string? Currency { get; init; }
    }
}

/// <summary>R3, R4: the request shape of adjustments R3, R6–R8, R10, R14, before any row is read.</summary>
public class RequestAdjustmentCommandValidator : AbstractValidator<RequestAdjustmentCommand>
{
    internal const string NeitherMessage = "At least one of amount and security is required.";

    private static readonly string[] ReasonNames = Enum.GetNames<AdjustmentReason>();

    public RequestAdjustmentCommandValidator()
    {
        RuleFor(command => command.AccountId).NotEmpty();
        RuleFor(command => command).Custom((command, context) =>
        {
            if (command.Amount is null && command.Security is null)
            {
                context.AddFailure(new ValidationFailure(string.Empty, NeitherMessage));
            }
        });
        RuleFor(command => command.Reason)
            .Must(reason => reason is not null && ReasonNames.Contains(reason, StringComparer.Ordinal))
            .WithMessage($"Reason must be one of {string.Join(", ", ReasonNames)}.");
        RuleFor(command => command.Memo)
            .Must(memo => memo is not null && memo.Trim().Length is >= 1 and <= AdjustmentRequest.MemoMaxLength)
            .WithMessage("Memo is required and must be 1 to 200 characters.");
        RuleFor(command => command.Reference)
            .Must(reference => string.IsNullOrWhiteSpace(reference) || reference.Trim().Length <= AdjustmentRequest.ReferenceMaxLength)
            .WithMessage("Reference must be at most 100 characters.");
        RuleFor(command => command.Amount)
            .NotEqual(0m)
            .When(command => command.Amount is not null)
            .WithMessage("Amount cannot be 0.");

        When(command => command.Security is not null, () =>
        {
            RuleFor(command => command.Security!.InstrumentId).NotEmpty();
            RuleFor(command => command.Security)
                .Must(security => security!.QuantityChange != 0m || security.CostChange != 0m)
                .WithMessage("At least one of quantityChange and costChange must be non-zero.");
            RuleFor(command => command.Security!.QuantityChange)
                .Must(change => FxConversionPricing.FitsDecimalPlaces(change, 8))
                .WithMessage("Quantity change can have at most 8 decimal places.");
            RuleFor(command => command.Security!.CostChange)
                .Must(change => FxConversionPricing.FitsDecimalPlaces(change, 4))
                .WithMessage("Cost change can have at most 4 decimal places.");
            RuleFor(command => command.Security!.Currency)
                .Null()
                .WithMessage("Currency cannot be set on an adjustment. The instrument's quote currency is used.");
        });

        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(command => command.BookedAt).Null().WithMessage(BookedAtByCustody.CustodialMessage);
        RuleFor(command => command.Type).Null().WithMessage("Type cannot be set on an adjustment.");
        RuleFor(command => command.Currency)
            .Null()
            .WithMessage("Currency cannot be set on an adjustment. The account currency is used.");
        RuleFor(command => command.IdempotencyKey)
            .Must(key => Guid.TryParse(key, out _))
            .WithMessage("Idempotency-Key is required and must be a UUID.");
    }
}

public class RequestAdjustmentCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrencyCatalog catalog,
    TimeProvider time)
    : IRequestHandler<RequestAdjustmentCommand, IdempotentResult>
{
    private const string DefaultPath = "/adjustment-requests";

    public async Task<IdempotentResult> Handle(RequestAdjustmentCommand request, CancellationToken cancellationToken)
    {
        var tenant = await AdjustmentRequestChange.TenantAsync(db, currentUser, cancellationToken);
        var actor = AdjustmentRequestChange.ActorId(currentUser);
        var key = Guid.Parse(request.IdempotencyKey!);
        var hash = TransactionRequestHash.Compute(
            request.RequestMethod ?? "POST",
            request.RequestPath ?? DefaultPath,
            new
            {
                accountId = request.AccountId,
                reason = request.Reason,
                memo = request.Memo,
                reference = request.Reference,
                amount = request.Amount,
                security = request.Security is null
                    ? null
                    : new
                    {
                        instrumentId = request.Security.InstrumentId,
                        quantityChange = request.Security.QuantityChange,
                        costChange = request.Security.CostChange
                    }
            });

        IdempotentResult? result = null;
        try
        {
            await db.ExecuteInTransactionAsync(async token =>
            {
                result = await RequestAsync(request, tenant, actor, key, hash, token);
            }, cancellationToken);
        }
        catch (ValidationException exception) when (IsUniqueConstraint(exception))
        {
            db.ClearTrackedChanges();
            var existing = await FindRecordAsync(tenant.Id, key, cancellationToken);
            if (existing is not null)
            {
                return ReplayOrConflict(existing, hash);
            }

            throw;
        }

        return result!;
    }

    /// <summary>R4: the checks of an adjustment on the locked state, in the order of adjustments §4.2. A refusal
    /// stores nothing.</summary>
    private async Task<IdempotentResult> RequestAsync(
        RequestAdjustmentCommand request,
        Tenant tenant,
        int actor,
        Guid key,
        string hash,
        CancellationToken cancellationToken)
    {
        AdjustmentPosting.EnsureTenantActive(tenant);

        var (account, customer) = await LoadAccountAsync(request.AccountId!.Value, tenant.Id, cancellationToken);
        await db.LockAccountAsync(account.Id, cancellationToken);
        await AdjustmentPosting.EnsureAccountAcceptsAsync(db, account, customer, cancellationToken);

        var existing = await FindRecordAsync(tenant.Id, key, cancellationToken);
        if (existing is not null)
        {
            return ReplayOrConflict(existing, hash);
        }

        AdjustmentHoldingChange? holdingChange = null;
        if (request.Security is { } security)
        {
            var instrument = await db.Instruments.SingleOrDefaultAsync(
                                 row => row.PublicId == security.InstrumentId && row.TenantId == tenant.Id,
                                 cancellationToken)
                             ?? throw new NotFoundException("Instrument", security.InstrumentId);
            holdingChange = await AdjustmentPosting.HoldingChangeAsync(
                db, instrument, account.Id, security.QuantityChange, security.CostChange, cancellationToken);
        }

        var reason = Enum.Parse<AdjustmentReason>(request.Reason!);
        var now = time.GetUtcNow();
        // R4: the Transaction the approval would post, built to run its checks, and never stored.
        await AdjustmentPosting.BuildAsync(
            db, catalog, tenant.Id, account, reason, request.Memo!, request.Reference, request.Amount, holdingChange, now,
            TransactionSource.AdjustmentRequest(Guid.NewGuid()), cancellationToken);
        var sumAfter = await TransactionCash.SumAsync(db, account.Id, cancellationToken) + (request.Amount ?? 0m);
        await AdjustmentPosting.EnsureCashAfterAsync(db, catalog, account, sumAfter, cancellationToken);

        var adjustmentRequest = AdjustmentRequest.Request(
            tenant.Id,
            account.Id,
            reason,
            request.Memo!,
            request.Reference,
            request.Amount,
            holdingChange is null ? null : (holdingChange.InstrumentId, holdingChange.QuantityChange, holdingChange.CostChange),
            actor,
            now);
        db.AdjustmentRequests.Add(adjustmentRequest);
        var body = JsonSerializer.Serialize(new { id = adjustmentRequest.PublicId });
        db.IdempotencyRecords.Add(IdempotencyRecord.Create(
            tenant.Id,
            key,
            request.RequestMethod ?? "POST",
            request.RequestPath ?? DefaultPath,
            hash,
            201,
            body,
            now));
        await db.SaveChangesAsync(cancellationToken);
        return new IdempotentResult(201, body);
    }

    /// <summary>R1: the Account in the caller's tenant, else 404.</summary>
    private async Task<(Account Account, User Customer)> LoadAccountAsync(
        Guid accountPublicId, int tenantId, CancellationToken cancellationToken)
    {
        var row = await (
                      from account in db.Accounts
                      join customer in db.Users on account.CustomerId equals customer.Id
                      where account.PublicId == accountPublicId && account.TenantId == tenantId
                      select new { account, customer }).SingleOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException("Account", accountPublicId);
        return (row.account, row.customer);
    }

    private static IdempotentResult ReplayOrConflict(IdempotencyRecord existing, string hash)
    {
        if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException();
        }

        return new IdempotentResult(existing.ResponseStatus, existing.ResponseBody);
    }

    private Task<IdempotencyRecord?> FindRecordAsync(int tenantId, Guid key, CancellationToken cancellationToken) =>
        db.IdempotencyRecords.SingleOrDefaultAsync(
            record => record.TenantId == tenantId && record.Key == key,
            cancellationToken);

    private static bool IsUniqueConstraint(ValidationException exception) =>
        exception.Errors.Values.Any(messages => messages.Contains("A unique constraint was violated."));
}
