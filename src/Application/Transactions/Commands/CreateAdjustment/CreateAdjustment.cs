using MyWealthV2.Application.AccountClosures;
using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Transactions.Commands.CreateAdjustment;

/// <summary>Adjustments §7: a TenantAdmin's correction of the cash, one holding, or both, on one Custodial account.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public class CreateAdjustmentCommand : IRequest<IdempotentResult>
{
    public Guid? AccountId { get; init; }

    public string? Reason { get; init; }

    public string? Memo { get; init; }

    public string? Reference { get; init; }

    public decimal? Amount { get; init; }

    public AdjustmentSecurity? Security { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Not part of the body (R14): the server books an adjustment.</summary>
    public DateTimeOffset? BookedAt { get; init; }

    /// <summary>Not part of the body: the route names the type.</summary>
    public string? Type { get; init; }

    /// <summary>Not part of the body (R9): the Account currency is used.</summary>
    public string? Currency { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }

    /// <summary>The holding part (R10): one instrument and the signed changes.</summary>
    public sealed class AdjustmentSecurity
    {
        public Guid InstrumentId { get; init; }

        public decimal QuantityChange { get; init; }

        public decimal CostChange { get; init; }

        /// <summary>Not part of the body (R10): the instrument's quote currency is used.</summary>
        public string? Currency { get; init; }
    }
}

/// <summary>Adjustments R3, R6–R8, R10 (decimal places), R14: the request shape, before any row is read.</summary>
public class CreateAdjustmentCommandValidator : AbstractValidator<CreateAdjustmentCommand>
{
    internal const string NeitherMessage = "At least one of amount and security is required.";

    private static readonly string[] ReasonNames = Enum.GetNames<AdjustmentReason>();

    public CreateAdjustmentCommandValidator()
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
            .Must(memo => memo is not null && memo.Trim().Length is >= 1 and <= 200)
            .WithMessage("Memo is required and must be 1 to 200 characters.");
        RuleFor(command => command.Reference)
            .Must(reference => string.IsNullOrWhiteSpace(reference) || reference.Trim().Length <= 100)
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

public class CreateAdjustmentCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrencyCatalog catalog,
    TimeProvider time)
    : IRequestHandler<CreateAdjustmentCommand, IdempotentResult>
{
    private const string DefaultPath = "/transactions/adjustment";

    public async Task<IdempotentResult> Handle(CreateAdjustmentCommand request, CancellationToken cancellationToken)
    {
        var tenant = await ResolveTenantAsync(cancellationToken);
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
                result = await CreateAsync(request, tenant, key, hash, token);
            }, cancellationToken);
        }
        catch (ValidationException exception) when (IsUniqueConstraint(exception))
        {
            var existing = await FindRecordAsync(tenant.Id, key, cancellationToken);
            if (existing is not null)
            {
                return ReplayOrConflict(existing, hash);
            }

            throw;
        }

        return result!;
    }

    /// <summary>The order of adjustments §4.2: Account, its state, custody mode, idempotency, instrument, balances.</summary>
    private async Task<IdempotentResult> CreateAsync(
        CreateAdjustmentCommand request,
        Tenant tenant,
        Guid key,
        string hash,
        CancellationToken cancellationToken)
    {
        if (!tenant.IsActive)
        {
            throw new TargetDisabledException(
                "tenant",
                tenant.PublicId,
                "Tenant is disabled",
                "Cannot create a transaction while the tenant is disabled. Enable the tenant first.");
        }

        // R1: the Account in the caller's tenant (else 404), locked before anything is read from it.
        var (account, customer) = await LoadAccountAsync(request.AccountId!.Value, tenant.Id, cancellationToken);
        await db.LockAccountAsync(account.Id, cancellationToken);

        // R15
        if (account.Status == AccountStatus.Closed)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cannot post a transaction while the account is closed. Reopen the account first.")
            ]);
        }

        // Account closure R12: a closing account accepts no adjustment.
        await AccountClosing.EnsureNotClosingAsync(db, account.Id, cancellationToken);

        if (customer.Status == UserStatus.Disabled)
        {
            throw new TargetDisabledException(
                "user",
                customer.PublicId,
                "User is disabled",
                "Cannot create a transaction while the customer is disabled. Enable the customer first.");
        }

        // R5: the manual entry matrix row for Adjustment.
        var refusal = CustodyRules.RefusalMessage(account.CustodyMode, TransactionType.Adjustment);
        if (refusal is not null)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateAdjustmentCommand.AccountId), refusal)
            ]);
        }

        // R20
        var existing = await FindRecordAsync(tenant.Id, key, cancellationToken);
        if (existing is not null)
        {
            return ReplayOrConflict(existing, hash);
        }

        var holdingChange = request.Security is null
            ? null
            : await HoldingChangeAsync(request.Security, tenant.Id, account.Id, cancellationToken);

        // Orders R40: an adjustment that lowers a holding leaves its available quantity at or above zero.
        if (holdingChange is { QuantityChange: < 0m })
        {
            var reserved = await Orders.OrderReservations.ReservedQuantityAsync(
                db, account.Id, holdingChange.InstrumentId, cancellationToken);
            if (reserved > 0m
                && !AvailableQuantity.Allows(holdingChange.CurrentQuantity + holdingChange.QuantityChange, reserved))
            {
                var symbol = await db.Instruments.AsNoTracking()
                    .Where(row => row.Id == holdingChange.InstrumentId)
                    .Select(row => row.Symbol)
                    .SingleAsync(cancellationToken);
                throw new ValidationException([
                    new ValidationFailure(
                        nameof(CreateAdjustmentCommand.Security),
                        $"{Orders.OrderChecks.Units(reserved)} {symbol} is reserved for sell orders.")
                ]);
            }
        }

        if (request.Amount is { } amount)
        {
            CashAmountPlaces.EnsureFits(catalog, Money.Create(0m, account.Currency).Currency, amount);
        }

        var currentSum = await TransactionCash.SumAsync(db, account.Id, cancellationToken);
        Domain.Entities.Transaction transaction;
        try
        {
            // R13, custody-modes R12: the caller's numbers as given, booked at the server clock.
            transaction = Domain.Entities.Transaction.PostAdjustment(
                tenant.Id,
                account.Id,
                account.Type,
                account.Currency,
                Enum.Parse<AdjustmentReason>(request.Reason!),
                request.Memo,
                request.Reference,
                time.GetUtcNow(),
                TransactionSource.Manual,
                request.Amount,
                currentSum,
                holdingChange);
        }
        catch (DomainException exception)
        {
            var field = exception.Message == Domain.Entities.Transaction.AdjustmentHoldingMessage
                ? nameof(CreateAdjustmentCommand.Security)
                : string.Empty;
            throw new ValidationException([new ValidationFailure(field, exception.Message)]);
        }

        // R16: no portfolio trade lock and no rebalance; the next rebalance sees the adjusted holding.
        db.Transactions.Add(transaction);
        var body = JsonSerializer.Serialize(new { id = transaction.PublicId });
        db.IdempotencyRecords.Add(IdempotencyRecord.Create(
            tenant.Id,
            key,
            request.RequestMethod ?? "POST",
            request.RequestPath ?? DefaultPath,
            hash,
            201,
            body,
            time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);

        if (transaction.SecurityLegs.Count > 0)
        {
            await HoldingProjection.SyncAsync(
                db, tenant.Id, account.Id, transaction.SecurityLegs.Select(leg => leg.InstrumentId), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        var sumAfter = await TransactionCash.SumAsync(db, account.Id, cancellationToken);
        if (sumAfter < 0)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cash balance cannot become negative.")
            ]);
        }

        await AvailableCashGuard.EnsureAsync(db, catalog, account, sumAfter, cancellationToken);

        return new IdempotentResult(201, body);
    }

    /// <summary>R10, R12: the instrument in the tenant (else 404) and the holding before the adjustment.</summary>
    private async Task<AdjustmentHoldingChange> HoldingChangeAsync(
        CreateAdjustmentCommand.AdjustmentSecurity security,
        int tenantId,
        int accountId,
        CancellationToken cancellationToken)
    {
        var instrument = await db.Instruments.SingleOrDefaultAsync(
                             row => row.PublicId == security.InstrumentId && row.TenantId == tenantId,
                             cancellationToken)
                         ?? throw new NotFoundException("Instrument", security.InstrumentId);
        var holding = await db.Holdings.SingleOrDefaultAsync(
            row => row.AccountId == accountId && row.InstrumentId == instrument.Id,
            cancellationToken);

        // R12: a disabled instrument only corrects a position the Account already holds.
        if (!instrument.IsActive && holding is null)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateAdjustmentCommand.Security), "Instrument is disabled.")
            ]);
        }

        return new AdjustmentHoldingChange(
            instrument.Id,
            instrument.QuoteCurrency.Trim(),
            security.QuantityChange,
            security.CostChange,
            holding?.Quantity ?? 0m,
            holding?.CostAmount ?? 0m);
    }

    private async Task<Tenant> ResolveTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");
        return await db.Tenants.SingleOrDefaultAsync(row => row.Id == tenantId, cancellationToken)
               ?? throw new NotFoundException("Tenant", tenantId);
    }

    private async Task<(Account Account, User Customer)> LoadAccountAsync(
        Guid accountPublicId,
        int tenantId,
        CancellationToken cancellationToken)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var row = await (
                      from account in db.Accounts
                      join customer in db.Users on account.CustomerId equals customer.Id
                      where account.PublicId == accountPublicId
                            && account.TenantId == tenantId
                            && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
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
