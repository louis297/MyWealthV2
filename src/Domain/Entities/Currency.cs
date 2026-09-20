using MyWealthV2.Domain.Exceptions;

namespace MyWealthV2.Domain.Entities;

public class Currency
{
    private Currency()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int DecimalPlaces { get; private set; }

    public bool IsActive { get; private set; }

    public static Currency Create(string code, string name, int decimalPlaces, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != 3 || !code.Trim().All(char.IsAsciiLetter))
        {
            throw new DomainException("Currency code must be three letters.");
        }

        if (decimalPlaces is < 0 or > 4)
        {
            throw new DomainException("DecimalPlaces must be between 0 and 4.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Currency name is required.");
        }

        return new Currency
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            DecimalPlaces = decimalPlaces,
            IsActive = isActive
        };
    }
}
