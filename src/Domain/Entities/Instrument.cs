namespace MyWealthV2.Domain.Entities;

public class Instrument : BaseAuditableEntity
{
    private Instrument()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public string Symbol { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string QuoteCurrency { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    public bool IsActive { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static Instrument Create(
        int tenantId,
        string symbol,
        string name,
        Currency quoteCurrency,
        Guid? publicId = null)
    {
        EnsureQuoteCurrency(quoteCurrency);

        var instrument = new Instrument
        {
            TenantId = tenantId,
            Symbol = NormaliseSymbol(symbol),
            Name = NormaliseName(name),
            QuoteCurrency = quoteCurrency.Code,
            PublicId = publicId ?? Guid.NewGuid(),
            IsEnabled = true
        };
        instrument.AddDomainEvent(new InstrumentCreated(instrument));
        return instrument;
    }

    public void Rename(string? name, string? symbol)
    {
        if (name is not null)
        {
            Name = NormaliseName(name);
        }

        if (symbol is not null)
        {
            Symbol = NormaliseSymbol(symbol);
        }
    }

    public void Disable()
    {
        if (!IsEnabled)
        {
            return;
        }

        IsEnabled = false;
        AddDomainEvent(new InstrumentDisabled(this));
    }

    public void Enable()
    {
        if (IsEnabled)
        {
            return;
        }

        IsEnabled = true;
        AddDomainEvent(new InstrumentEnabled(this));
    }

    private static string NormaliseSymbol(string symbol)
    {
        var normalised = symbol?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalised) || normalised.Length > 32
            || normalised.Any(ch => !char.IsAsciiLetterUpper(ch) && !char.IsAsciiDigit(ch) && ch is not '.' and not '-'))
        {
            throw new DomainException("Symbol must be 1 to 32 characters in [A-Z0-9.-].");
        }

        return normalised;
    }

    private static string NormaliseName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < 1 or > 200)
        {
            throw new DomainException("Name is required.");
        }

        return trimmed;
    }

    private static void EnsureQuoteCurrency(Currency currency)
    {
        if (currency is null || string.IsNullOrWhiteSpace(currency.Code))
        {
            throw new DomainException("Quote currency is required.");
        }

        if (!currency.IsEnabled)
        {
            throw new DomainException("Quote currency must be enabled.");
        }
    }
}
