using FluentValidation.Results;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Transactions;

/// <summary>FX conversions R6–R11 checks shared by create and quote.</summary>
internal static class FxConversionScope
{
    public static async Task<Tenant> ResolveTenantAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid? tenantPublicId,
        CancellationToken cancellationToken)
    {
        if (currentUser.Role == UserRole.SystemAdmin)
        {
            return await db.Tenants.SingleOrDefaultAsync(
                       row => row.PublicId == tenantPublicId, cancellationToken)
                   ?? throw new NotFoundException("Tenant", tenantPublicId!);
        }

        var tenantId = currentUser.TenantId
                       ?? throw new NotFoundException("Tenant", "current");
        return await db.Tenants.SingleOrDefaultAsync(row => row.Id == tenantId, cancellationToken)
               ?? throw new NotFoundException("Tenant", tenantId);
    }

    /// <summary>
    /// R6: both Accounts resolve in the tenant and the caller's scope, else 404.
    /// R7: then both belong to one Customer (else 400), who is not Disabled.
    /// </summary>
    public static async Task<FxAccountPair> LoadPairAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        int tenantId,
        Guid fromAccountId,
        Guid toAccountId,
        CancellationToken cancellationToken)
    {
        var from = await LoadAccountAsync(db, currentUser, tenantId, fromAccountId, cancellationToken);
        var to = await LoadAccountAsync(db, currentUser, tenantId, toAccountId, cancellationToken);

        if (from.Customer.Id != to.Customer.Id)
        {
            throw new ValidationException([
                new ValidationFailure(
                    "ToAccountId",
                    "The from and to accounts must belong to the same customer. Convert first, then transfer.")
            ]);
        }

        if (from.Customer.Status == UserStatus.Disabled)
        {
            throw new TargetDisabledException(
                "user",
                from.Customer.PublicId,
                "User is disabled",
                "Cannot create a transaction while the customer is disabled. Enable the customer first.");
        }

        return new FxAccountPair(from.Account, to.Account, from.Customer);
    }

    /// <summary>R4, R8 (state), R9–R11: currencies, the stored rate, staleness and decimal places.</summary>
    public static async Task<FxPrice> PriceAsync(
        IFxRate fxRates,
        ICurrencyCatalog currencies,
        FxAccountPair pair,
        decimal? sellAmount,
        decimal? buyAmount,
        DateTimeOffset bookedAt,
        CancellationToken cancellationToken)
    {
        var sellCurrency = Money.Create(0m, pair.From.Currency).Currency;
        var buyCurrency = Money.Create(0m, pair.To.Currency).Currency;
        if (string.Equals(sellCurrency, buyCurrency, StringComparison.Ordinal))
        {
            throw new ValidationException([
                new ValidationFailure(
                    "ToAccountId",
                    "The from and to accounts must have different currencies. Use POST /transactions/transfer.")
            ]);
        }

        var sellPlaces = DecimalPlaces(currencies, sellCurrency);
        var buyPlaces = DecimalPlaces(currencies, buyCurrency);
        if (sellAmount is { } sell && !FxConversionPricing.FitsDecimalPlaces(sell, sellPlaces))
        {
            throw new ValidationException([
                new ValidationFailure("SellAmount", $"Sell amount can have at most {sellPlaces} decimal places.")
            ]);
        }

        if (buyAmount is { } buy && !FxConversionPricing.FitsDecimalPlaces(buy, buyPlaces))
        {
            throw new ValidationException([
                new ValidationFailure("BuyAmount", $"Buy amount can have at most {buyPlaces} decimal places.")
            ]);
        }

        FxRateReading reading;
        try
        {
            reading = await fxRates.GetRateDetailAsync(sellCurrency, buyCurrency, bookedAt, cancellationToken);
        }
        catch (FxRateNotFoundException)
        {
            throw new ValidationException([
                new ValidationFailure(
                    "BookedAt",
                    $"No {sellCurrency} to {buyCurrency} rate is available on or before {FxConversionPricing.DateOf(bookedAt):yyyy-MM-dd}.")
            ]);
        }

        if (FxConversionPricing.IsStale(reading.AsOfDate, bookedAt))
        {
            throw new ValidationException([
                new ValidationFailure(
                    "BookedAt",
                    $"The {sellCurrency} to {buyCurrency} rate is from {reading.AsOfDate:yyyy-MM-dd}, more than {FxConversionPricing.MaxRateAgeDays} days before the booked date.")
            ]);
        }

        var rate = AppliedFxRate.Create(sellCurrency, buyCurrency, reading.Rate, reading.AsOfDate, reading.Source);
        return sellAmount is { } fixedSell
            ? new FxPrice(rate, Money.Create(fixedSell, sellCurrency), buyPlaces)
            : new FxPrice(rate, Money.Create(buyAmount!.Value, buyCurrency), sellPlaces);
    }

    private static int DecimalPlaces(ICurrencyCatalog currencies, string code) =>
        currencies.TryGet(code)?.DecimalPlaces
        ?? throw new ValidationException([
            new ValidationFailure(string.Empty, $"Currency {code} is not in the catalog.")
        ]);

    private static async Task<(Account Account, User Customer)> LoadAccountAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        int tenantId,
        Guid accountPublicId,
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
}

internal sealed record FxAccountPair(Account From, Account To, User Customer);

/// <summary>The rate to apply, the side the caller named, and the decimal places of the computed side.</summary>
internal sealed record FxPrice(AppliedFxRate Rate, Money FixedAmount, int ComputedDecimalPlaces);
