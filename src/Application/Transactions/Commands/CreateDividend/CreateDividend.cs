using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.AccountClosures;
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

namespace MyWealthV2.Application.Transactions.Commands.CreateDividend;

/// <summary>
/// External income §4.3: a dividend an outside party paid into an External account, as the caller states it
/// (<c>POST /transactions/dividend</c>). A field the body does not have is refused (R11).
/// </summary>
[Authorize(Policy = Policies.TransactionsCreate)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateDividendCommand : IRequest<IdempotentResult>
{
    public Guid? TenantId { get; init; }

    public Guid? AccountId { get; init; }

    public Guid? InstrumentId { get; init; }

    /// <summary>The shares the dividend was paid on.</summary>
    public decimal? Quantity { get; init; }

    /// <summary>The amount the company announced per share, in the quote currency; stored, never multiplied.</summary>
    public decimal? AmountPerShare { get; init; }

    /// <summary>The dividend before any tax, in the quote currency.</summary>
    public decimal? GrossAmount { get; init; }

    public string? TaxSource { get; init; }

    public decimal? ImputationCredit { get; init; }

    public List<EnteredTaxBody>? Taxes { get; init; }

    /// <summary>Account currency per 1 unit of quote currency, when they differ (R14).</summary>
    public decimal? FxRate { get; init; }

    public DateTimeOffset? BookedAt { get; init; }

    public DateOnly? ExDate { get; init; }

    public DateOnly? RecordDate { get; init; }

    public DateOnly? PaymentDate { get; init; }

    public string? Memo { get; init; }

    public string? Reference { get; init; }

    /// <summary>Not part of the body (R11). Declared only so that a request that sends it is refused at the field.</summary>
    public string? Type { get; init; }

    /// <summary>Not part of the body (R11).</summary>
    public decimal? Amount { get; init; }

    /// <summary>Not part of the body (R11).</summary>
    public string? Currency { get; init; }

    /// <summary>Not part of the body (R11).</summary>
    public JsonElement? Cash { get; init; }

    /// <summary>Not part of the body (R11).</summary>
    public JsonElement? Security { get; init; }

    /// <summary>Not part of the body (R11).</summary>
    public decimal? Fee { get; init; }

    [JsonIgnore]
    public string? IdempotencyKey { get; set; }

    [JsonIgnore]
    public string? RequestMethod { get; set; }

    [JsonIgnore]
    public string? RequestPath { get; set; }
}

public class CreateDividendCommandValidator : AbstractValidator<CreateDividendCommand>
{
    internal const string TaxSourceMessage = "TaxSource must be NewZealand or Foreign.";

    public CreateDividendCommandValidator(ICurrentUser currentUser, TimeProvider time)
    {
        RuleFor(command => command.AccountId).NotEmpty();
        RuleFor(command => command.InstrumentId).NotEmpty();
        RuleFor(command => command.Quantity)
            .Must(quantity => quantity is > 0m && FxConversionPricing.FitsDecimalPlaces(quantity.Value, 8))
            .WithMessage("Quantity must be greater than 0, with at most 8 decimal places.");
        RuleFor(command => command.GrossAmount)
            .Must(gross => gross is > 0m)
            .WithMessage("GrossAmount must be greater than 0.");
        RuleFor(command => command.AmountPerShare)
            .Must(amount => amount is > 0m and <= Domain.CorporateActions.DividendTerms.MaxAmountPerShare
                            && FxConversionPricing.FitsDecimalPlaces(amount.Value, 8))
            .When(command => command.AmountPerShare is not null)
            .WithMessage("AmountPerShare must be above 0 and at most 1 000 000, with at most 8 decimal places.");
        RuleFor(command => command.TaxSource)
            .Must(source => ParseTaxSource(source) is not null)
            .WithMessage(TaxSourceMessage);
        RuleFor(command => command.ImputationCredit)
            .GreaterThanOrEqualTo(0m)
            .When(command => command.ImputationCredit is not null)
            .WithMessage("ImputationCredit cannot be negative.");
        RuleFor(command => command.ImputationCredit)
            .Must((command, credit) => credit <= command.GrossAmount)
            .When(command => command.ImputationCredit is > 0m && command.GrossAmount is > 0m)
            .WithMessage("ImputationCredit cannot be more than the gross amount.");
        RuleFor(command => command.ImputationCredit)
            .Must((command, _) => ParseTaxSource(command.TaxSource) == DividendTaxSource.NewZealand)
            .When(command => command.ImputationCredit is > 0m && ParseTaxSource(command.TaxSource) is not null)
            .WithMessage("Only a New Zealand company attaches imputation credits.");

        EnteredTaxRules.Add(this, command => command.Taxes);
        RuleFor(command => command.Taxes).Custom((rows, context) =>
        {
            var source = ParseTaxSource(context.InstanceToValidate.TaxSource);
            if (source is null || EnteredTaxBody.Parse(rows) is not { } taxes || EnteredTaxes.RefusalOf(taxes) is not null)
            {
                return;
            }

            if (EnteredTaxes.RefusalOf(taxes, source.Value) is { } refusal)
            {
                context.AddFailure(refusal.Field, refusal.Message);
            }
        });

        RuleFor(command => command.FxRate)
            .GreaterThan(0m)
            .When(command => command.FxRate is not null)
            .WithMessage("FxRate must be greater than 0.");
        RuleFor(command => command.FxRate)
            .Must(rate => FxConversionPricing.FitsDecimalPlaces(rate!.Value, 8))
            .When(command => command.FxRate is > 0m)
            .WithMessage("FxRate can have at most 8 decimal places.");
        RuleFor(command => command.BookedAt).NotInTheFuture(time);
        RuleFor(command => command.Memo)
            .Must(memo => memo is null || memo.Trim().Length is >= 1 and <= 200)
            .WithMessage("Memo must be 1 to 200 characters.");
        RuleFor(command => command.Reference)
            .Must(reference => string.IsNullOrWhiteSpace(reference) || reference.Trim().Length <= 100)
            .WithMessage("Reference must be at most 100 characters.");
        RuleFor(command => command.IdempotencyKey)
            .Must(key => Guid.TryParse(key, out _))
            .WithMessage("Idempotency-Key is required and must be a UUID.");

        RuleFor(command => command.Type).Null().WithMessage("Type cannot be set on a dividend.");
        RuleFor(command => command.Amount).Null().WithMessage("Amount cannot be set on a dividend. Use grossAmount.");
        RuleFor(command => command.Currency).Null()
            .WithMessage("Currency cannot be set on a dividend. It is the instrument's quote currency.");
        RuleFor(command => command.Cash).Null().WithMessage("Cash cannot be set on a dividend.");
        RuleFor(command => command.Security).Null().WithMessage("Security cannot be set on a dividend.");
        RuleFor(command => command.Fee).Null().WithMessage("Fee cannot be set on a dividend.");

        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(command => command.TenantId).NotEmpty().WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(command => command.TenantId)
                .Must(tenantId => tenantId is null)
                .WithMessage("TenantId cannot be set.");
        });
    }

    /// <summary>R11: the tax source by its exact name (dividends R3), or null.</summary>
    internal static DividendTaxSource? ParseTaxSource(string? name) => name switch
    {
        nameof(DividendTaxSource.NewZealand) => DividendTaxSource.NewZealand,
        nameof(DividendTaxSource.Foreign) => DividendTaxSource.Foreign,
        _ => null
    };
}

public class CreateDividendCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrencyCatalog currencies,
    TimeProvider time)
    : IRequestHandler<CreateDividendCommand, IdempotentResult>
{
    private const string DefaultPath = "/transactions/dividend";

    public async Task<IdempotentResult> Handle(CreateDividendCommand request, CancellationToken cancellationToken)
    {
        var tenant = await ResolveTenantAsync(request.TenantId, cancellationToken);
        var key = Guid.Parse(request.IdempotencyKey!);
        // R20: the path and every field of the body are in the hash.
        var hash = TransactionRequestHash.Compute(
            request.RequestMethod ?? "POST",
            request.RequestPath ?? DefaultPath,
            new
            {
                accountId = request.AccountId,
                instrumentId = request.InstrumentId,
                quantity = request.Quantity,
                amountPerShare = request.AmountPerShare,
                grossAmount = request.GrossAmount,
                taxSource = request.TaxSource,
                imputationCredit = request.ImputationCredit,
                taxes = request.Taxes?.Select(tax => new { kind = tax.Kind, ratePercent = tax.RatePercent, amount = tax.Amount }),
                fxRate = request.FxRate,
                bookedAt = request.BookedAt?.ToString("o"),
                exDate = request.ExDate?.ToString("yyyy-MM-dd"),
                recordDate = request.RecordDate?.ToString("yyyy-MM-dd"),
                paymentDate = request.PaymentDate?.ToString("yyyy-MM-dd"),
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
        CreateDividendCommand request,
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

        // R1, R3: the Account in scope, else 404; then locked, and the Account rules of posting R6, R7.
        var (account, customer) = await LoadAccountAsync(request.AccountId!.Value, tenant.Id, cancellationToken);
        await db.LockAccountAsync(account.Id, cancellationToken);
        if (account.Status == AccountStatus.Closed)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cannot post a transaction while the account is closed. Reopen the account first.")
            ]);
        }

        await AccountClosing.EnsureNotClosingAsync(db, account.Id, cancellationToken);
        if (customer.Status == UserStatus.Disabled)
        {
            throw new TargetDisabledException(
                "user",
                customer.PublicId,
                "User is disabled",
                "Cannot create a transaction while the customer is disabled. Enable the customer first.");
        }

        // R2, R21: a Custodial account's dividends are paid by the platform.
        if (CustodyRules.RefusalMessage(account.CustodyMode, TransactionType.Dividend) is { } refusal)
        {
            throw new ValidationException([new ValidationFailure(nameof(CreateDividendCommand.AccountId), refusal)]);
        }

        // R15.
        var bookedAt = BookedAtByCustody.Resolve(account.CustodyMode, request.BookedAt, time);

        // R10: an Instrument of the tenant; a disabled one is allowed, nothing is bought.
        var instrument = await db.Instruments.SingleOrDefaultAsync(
                             row => row.PublicId == request.InstrumentId && row.TenantId == tenant.Id, cancellationToken)
                         ?? throw new NotFoundException("Instrument", request.InstrumentId!.Value);

        var accountCurrency = Money.Create(0m, account.Currency).Currency;
        var quote = Money.Create(0m, instrument.QuoteCurrency).Currency;
        var quotePlaces = currencies.TryGet(quote)?.DecimalPlaces ?? 2;
        var accountPlaces = currencies.TryGet(accountCurrency)?.DecimalPlaces ?? 2;
        var taxes = EnteredTaxBody.Parse(request.Taxes)!;

        // R13, R14.
        var failures = new List<ValidationFailure>();
        if (!FxConversionPricing.FitsDecimalPlaces(request.GrossAmount!.Value, quotePlaces))
        {
            failures.Add(new(nameof(CreateDividendCommand.GrossAmount), $"GrossAmount can have at most {quotePlaces} decimal places."));
        }

        if (request.ImputationCredit is { } credit && !FxConversionPricing.FitsDecimalPlaces(credit, quotePlaces))
        {
            failures.Add(new(nameof(CreateDividendCommand.ImputationCredit), $"ImputationCredit can have at most {quotePlaces} decimal places."));
        }

        failures.AddRange(EnteredTaxRules.DecimalPlaceFailures(taxes, quotePlaces));
        var crossCurrency = !string.Equals(quote, accountCurrency, StringComparison.Ordinal);
        if (crossCurrency && request.FxRate is null)
        {
            failures.Add(new(nameof(CreateDividendCommand.FxRate), "FxRate is required when the instrument currency differs from the account currency."));
        }
        else if (!crossCurrency && request.FxRate is not null)
        {
            failures.Add(new(nameof(CreateDividendCommand.FxRate), "FxRate cannot be set when the instrument currency is the account currency."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var appliedRate = crossCurrency
            ? AppliedFxRate.Create(quote, accountCurrency, request.FxRate!.Value, FxConversionPricing.DateOf(bookedAt), AppliedFxRate.CallerSource)
            : null;

        Domain.Entities.Transaction transaction;
        try
        {
            transaction = Domain.Entities.Transaction.PostEnteredDividend(
                tenant.Id,
                account.Id,
                accountCurrency,
                accountPlaces,
                instrument.Id,
                quote,
                request.Quantity!.Value,
                request.AmountPerShare,
                request.GrossAmount.Value,
                CreateDividendCommandValidator.ParseTaxSource(request.TaxSource)!.Value,
                request.ImputationCredit ?? 0m,
                taxes,
                appliedRate,
                request.ExDate,
                request.RecordDate,
                request.PaymentDate,
                bookedAt,
                request.Memo,
                request.Reference,
                TransactionSource.Manual);
        }
        catch (DomainException exception)
        {
            // R7, R14: the rules that hold only after the conversion are at their field.
            var field = exception.Message switch
            {
                EnteredTaxes.AboveGrossMessage => nameof(CreateDividendCommand.Taxes),
                Domain.Entities.Transaction.EnteredGrossConvertsTo0Message => nameof(CreateDividendCommand.GrossAmount),
                _ => string.Empty
            };
            throw new ValidationException([new ValidationFailure(field, exception.Message)]);
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

    private async Task<Tenant> ResolveTenantAsync(Guid? tenantPublicId, CancellationToken cancellationToken)
    {
        if (currentUser.Role == UserRole.SystemAdmin)
        {
            return await db.Tenants.SingleOrDefaultAsync(row => row.PublicId == tenantPublicId, cancellationToken)
                   ?? throw new NotFoundException("Tenant", tenantPublicId!);
        }

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

    private static bool IsUniqueConstraint(ValidationException exception) =>
        exception.Errors.Values.Any(messages => messages.Contains("A unique constraint was violated."));
}
