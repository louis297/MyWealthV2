using MyWealthV2.Domain.Exceptions;

namespace MyWealthV2.Domain.ValueObjects;

public class Money : ValueObject
{
    private Money()
    {
    }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public static Money Create(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new DomainException("Currency is required.");
        }

        return new Money
        {
            Amount = amount,
            Currency = currency.Trim().ToUpperInvariant()
        };
    }

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return Create(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return Create(Amount - other.Amount, Currency);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new DomainException("Cannot add or subtract amounts in different currencies.");
        }
    }
}
