using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Transactions.Commands.CreateFxConversion;

/// <summary>FX conversions §7: convert cash between two Accounts of one Customer in different currencies.</summary>
[Authorize(Policy = Policies.TransactionsCreate)]
public class CreateFxConversionCommand : IRequest<IdempotentResult>
{
    public Guid? TenantId { get; init; }

    public Guid? FromAccountId { get; init; }

    public Guid? ToAccountId { get; init; }

    public decimal? SellAmount { get; init; }

    public decimal? BuyAmount { get; init; }

    public decimal? ExpectedRate { get; init; }

    public DateTimeOffset? BookedAt { get; init; }

    public string? Memo { get; init; }

    public string? Reference { get; init; }

    /// <summary>Not part of the body (R3). Declared only so that a request that sends it is rejected.</summary>
    public string? Type { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public Guid? AccountId { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public string? Currency { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public decimal? Amount { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public decimal? Rate { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public JsonElement? Cash { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public JsonElement? Security { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public JsonElement? Legs { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }
}

public class CreateFxConversionCommandValidator : AbstractValidator<CreateFxConversionCommand>
{
    public CreateFxConversionCommandValidator(ICurrentUser currentUser)
    {
        RuleFor(command => command.FromAccountId).NotEmpty();
        RuleFor(command => command.ToAccountId).NotEmpty();
        RuleFor(command => command.ToAccountId)
            .Must((command, to) => to != command.FromAccountId)
            .When(command => command.FromAccountId is not null && command.ToAccountId is not null)
            .WithMessage("The from and to accounts must be different.");
        RuleFor(command => command)
            .Must(command => command.SellAmount is null != command.BuyAmount is null)
            .WithName(nameof(CreateFxConversionCommand.SellAmount))
            .WithMessage("Send exactly one of sellAmount and buyAmount.");
        RuleFor(command => command.SellAmount)
            .GreaterThan(0)
            .When(command => command.SellAmount is not null)
            .WithMessage("Sell amount must be greater than 0.");
        RuleFor(command => command.BuyAmount)
            .GreaterThan(0)
            .When(command => command.BuyAmount is not null)
            .WithMessage("Buy amount must be greater than 0.");
        RuleFor(command => command.ExpectedRate)
            .NotNull()
            .GreaterThan(0)
            .WithMessage("Expected rate is required. Get a quote first.");
        RuleFor(command => command.BookedAt).NotNull();
        RuleFor(command => command.Memo)
            .Must(memo => memo is null || memo.Trim().Length is >= 1 and <= 200)
            .WithMessage("Memo must be 1 to 200 characters.");
        RuleFor(command => command.Reference)
            .Must(reference => string.IsNullOrWhiteSpace(reference) || reference.Trim().Length <= 100)
            .WithMessage("Reference must be at most 100 characters.");
        RuleFor(command => command.IdempotencyKey)
            .Must(key => Guid.TryParse(key, out _))
            .WithMessage("Idempotency-Key is required and must be a UUID.");

        RuleFor(command => command.Type).Null().WithMessage("Type cannot be set on a conversion.");
        RuleFor(command => command.AccountId).Null().WithMessage("AccountId cannot be set on a conversion. Use fromAccountId and toAccountId.");
        RuleFor(command => command.Currency).Null().WithMessage("Currency cannot be set on a conversion. The account currencies are used.");
        RuleFor(command => command.Amount).Null().WithMessage("Amount cannot be set on a conversion. Use sellAmount or buyAmount.");
        RuleFor(command => command.Rate).Null().WithMessage("Rate cannot be set on a conversion. Use expectedRate from a quote.");
        RuleFor(command => command.Cash).Null().WithMessage("Cash cannot be set on a conversion.");
        RuleFor(command => command.Security).Null().WithMessage("Security cannot be set on a conversion.");
        RuleFor(command => command.Legs).Null().WithMessage("Legs cannot be set on a conversion.");

        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(command => command.TenantId)
                .NotEmpty()
                .WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(command => command.TenantId)
                .Must(tenantId => tenantId is null)
                .WithMessage("TenantId cannot be set.");
        });
    }
}

public class CreateFxConversionCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IFxRate fxRates,
    ICurrencyCatalog currencies)
    : IRequestHandler<CreateFxConversionCommand, IdempotentResult>
{
    private const string DefaultPath = "/transactions/fx";

    public async Task<IdempotentResult> Handle(CreateFxConversionCommand request, CancellationToken cancellationToken)
    {
        var tenant = await FxConversionScope.ResolveTenantAsync(db, currentUser, request.TenantId, cancellationToken);
        var key = Guid.Parse(request.IdempotencyKey!);
        var hash = TransactionRequestHash.Compute(
            request.RequestMethod ?? "POST",
            request.RequestPath ?? DefaultPath,
            new
            {
                fromAccountId = request.FromAccountId,
                toAccountId = request.ToAccountId,
                sellAmount = request.SellAmount,
                buyAmount = request.BuyAmount,
                expectedRate = request.ExpectedRate,
                bookedAt = request.BookedAt?.ToString("o"),
                memo = request.Memo,
                reference = request.Reference,
                tenantId = request.TenantId
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

    private async Task<IdempotentResult> CreateAsync(
        CreateFxConversionCommand request,
        Tenant tenant,
        Guid key,
        string hash,
        CancellationToken cancellationToken)
    {
        var existing = await FindRecordAsync(tenant.Id, key, cancellationToken);
        if (existing is not null)
        {
            return ReplayOrConflict(existing, hash);
        }

        if (!tenant.IsActive)
        {
            throw new TargetDisabledException(
                "tenant",
                tenant.PublicId,
                "Tenant is disabled",
                "Cannot create a transaction while the tenant is disabled. Enable the tenant first.");
        }

        // R6, R7: both Accounts in scope (else 404), then one Customer, not Disabled.
        var pair = await FxConversionScope.LoadPairAsync(
            db, currentUser, tenant.Id, request.FromAccountId!.Value, request.ToAccountId!.Value, cancellationToken);

        // R15: lock both Account rows in ascending Accounts.Id order before the rate is read.
        foreach (var accountId in new[] { pair.From.Id, pair.To.Id }.Order())
        {
            await db.LockAccountAsync(accountId, cancellationToken);
        }

        if (pair.From.Status == AccountStatus.Closed || pair.To.Status == AccountStatus.Closed)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cannot post a transaction while the account is closed. Reopen the account first.")
            ]);
        }

        var priced = await FxConversionScope.PriceAsync(
            fxRates, currencies, pair, request.SellAmount, request.BuyAmount, request.BookedAt!.Value, cancellationToken);

        // R12: the caller confirms the rate it was quoted.
        if (request.ExpectedRate!.Value != priced.Rate.Rate)
        {
            throw new ExpectedRateChangedException(priced.Rate.Rate);
        }

        var fromSum = await TransactionCash.SumAsync(db, pair.From.Id, cancellationToken);
        Domain.Entities.Transaction transaction;
        try
        {
            transaction = Domain.Entities.Transaction.PostFxConversion(
                tenant.Id,
                pair.From.Id,
                pair.From.Currency,
                pair.To.Id,
                pair.To.Currency,
                priced.Rate,
                priced.FixedAmount,
                priced.ComputedDecimalPlaces,
                request.BookedAt!.Value,
                request.Memo,
                request.Reference,
                fromSum);
        }
        catch (DomainException exception)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, exception.Message)
            ]);
        }

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
            DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(cancellationToken);

        if (await TransactionCash.SumAsync(db, pair.From.Id, cancellationToken) < 0)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cash balance cannot become negative.")
            ]);
        }

        return new IdempotentResult(201, body);
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
