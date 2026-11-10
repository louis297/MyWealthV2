using System.Text.Json;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Transactions.Queries.GetFxConversionQuote;

/// <summary>FX conversions R20: price a conversion without writing anything.</summary>
[Authorize(Policy = Policies.TransactionsCreate)]
public class GetFxConversionQuoteQuery : IRequest<FxConversionQuoteDto>
{
    public Guid? TenantId { get; init; }

    public Guid? FromAccountId { get; init; }

    public Guid? ToAccountId { get; init; }

    public decimal? SellAmount { get; init; }

    public decimal? BuyAmount { get; init; }

    /// <summary>Optional; defaults to now.</summary>
    public DateTimeOffset? BookedAt { get; init; }

    /// <summary>Not part of the quote body (R20).</summary>
    public decimal? ExpectedRate { get; init; }

    /// <summary>Not part of the quote body (R20).</summary>
    public string? Memo { get; init; }

    /// <summary>Not part of the quote body (R20).</summary>
    public string? Reference { get; init; }

    /// <summary>Not part of the body (R3).</summary>
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
}

public sealed record FxConversionQuoteDto(
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    string RateAsOfDate,
    FxQuoteAmountDto Sell,
    FxQuoteAmountDto Buy);

public sealed record FxQuoteAmountDto(decimal Amount, string Currency);

public class GetFxConversionQuoteQueryValidator : AbstractValidator<GetFxConversionQuoteQuery>
{
    public GetFxConversionQuoteQueryValidator(ICurrentUser currentUser)
    {
        RuleFor(query => query.FromAccountId).NotEmpty();
        RuleFor(query => query.ToAccountId).NotEmpty();
        RuleFor(query => query.ToAccountId)
            .Must((query, to) => to != query.FromAccountId)
            .When(query => query.FromAccountId is not null && query.ToAccountId is not null)
            .WithMessage("The from and to accounts must be different.");
        RuleFor(query => query)
            .Must(query => query.SellAmount is null != query.BuyAmount is null)
            .WithName(nameof(GetFxConversionQuoteQuery.SellAmount))
            .WithMessage("Send exactly one of sellAmount and buyAmount.");
        RuleFor(query => query.SellAmount)
            .GreaterThan(0)
            .When(query => query.SellAmount is not null)
            .WithMessage("Sell amount must be greater than 0.");
        RuleFor(query => query.BuyAmount)
            .GreaterThan(0)
            .When(query => query.BuyAmount is not null)
            .WithMessage("Buy amount must be greater than 0.");

        RuleFor(query => query.ExpectedRate).Null().WithMessage("ExpectedRate cannot be set on a quote.");
        RuleFor(query => query.Memo).Null().WithMessage("Memo cannot be set on a quote.");
        RuleFor(query => query.Reference).Null().WithMessage("Reference cannot be set on a quote.");
        RuleFor(query => query.Type).Null().WithMessage("Type cannot be set on a conversion.");
        RuleFor(query => query.AccountId).Null().WithMessage("AccountId cannot be set on a conversion. Use fromAccountId and toAccountId.");
        RuleFor(query => query.Currency).Null().WithMessage("Currency cannot be set on a conversion. The account currencies are used.");
        RuleFor(query => query.Amount).Null().WithMessage("Amount cannot be set on a conversion. Use sellAmount or buyAmount.");
        RuleFor(query => query.Rate).Null().WithMessage("Rate cannot be set on a conversion.");
        RuleFor(query => query.Cash).Null().WithMessage("Cash cannot be set on a conversion.");
        RuleFor(query => query.Security).Null().WithMessage("Security cannot be set on a conversion.");
        RuleFor(query => query.Legs).Null().WithMessage("Legs cannot be set on a conversion.");

        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId)
                .NotEmpty()
                .WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId)
                .Must(tenantId => tenantId is null)
                .WithMessage("TenantId cannot be set.");
        });
    }
}

/// <summary>Runs R1, R3–R11 like create; no lock, no cash SUM check, no write (R20).</summary>
public class GetFxConversionQuoteQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IFxRate fxRates,
    ICurrencyCatalog currencies,
    TimeProvider time)
    : IRequestHandler<GetFxConversionQuoteQuery, FxConversionQuoteDto>
{
    public async Task<FxConversionQuoteDto> Handle(GetFxConversionQuoteQuery request, CancellationToken cancellationToken)
    {
        var tenant = await FxConversionScope.ResolveTenantAsync(db, currentUser, request.TenantId, cancellationToken);
        var pair = await FxConversionScope.LoadPairAsync(
            db, currentUser, tenant.Id, request.FromAccountId!.Value, request.ToAccountId!.Value, cancellationToken);

        if (pair.From.Status == AccountStatus.Closed || pair.To.Status == AccountStatus.Closed)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cannot convert while the account is closed. Reopen the account first.")
            ]);
        }

        var bookedAt = request.BookedAt ?? time.GetUtcNow();
        var priced = await FxConversionScope.PriceAsync(
            fxRates, currencies, pair, request.SellAmount, request.BuyAmount, bookedAt, cancellationToken);

        decimal sell;
        decimal buy;
        try
        {
            (sell, buy) = Domain.Entities.Transaction.ComputeFxConversionAmounts(
                priced.Rate, priced.FixedAmount, priced.ComputedDecimalPlaces);
        }
        catch (DomainException exception)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, exception.Message)
            ]);
        }

        return new FxConversionQuoteDto(
            priced.Rate.BaseCurrency,
            priced.Rate.QuoteCurrency,
            priced.Rate.Rate,
            priced.Rate.AsOfDate.ToString("yyyy-MM-dd"),
            new FxQuoteAmountDto(sell, priced.Rate.BaseCurrency),
            new FxQuoteAmountDto(buy, priced.Rate.QuoteCurrency));
    }
}
