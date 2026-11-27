using MyWealthV2.Domain.Instruments;

namespace MyWealthV2.Domain.Entities;

public class Instrument : BaseAuditableEntity
{
    private readonly List<InstrumentFifTreatment> _fifTreatments = [];

    private Instrument()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public string Symbol { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string QuoteCurrency { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>Instrument-tax-attributes R4: the country the issuer is tax resident in, or none.</summary>
    public string? IssuerCountry { get; private set; }

    /// <summary>Instrument-tax-attributes R9: the FIF treatment rows, at most one per tax year.</summary>
    public IReadOnlyList<InstrumentFifTreatment> FifTreatments => _fifTreatments;

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
            IsActive = true
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
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        AddDomainEvent(new InstrumentDisabled(this));
    }

    public void Enable()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        AddDomainEvent(new InstrumentEnabled(this));
    }

    /// <summary>
    /// Instrument-tax-attributes R5, R9, R12, R16: replaces the issuer country and every FIF treatment row with what
    /// the caller states. Raises no event (R20).
    /// </summary>
    public void SetTaxAttributes(string? issuerCountry, IReadOnlyList<FifTreatmentInput> rows, int latestTaxYear)
    {
        if (TaxAttributeRefusals(issuerCountry, rows, latestTaxYear) is [var first, ..])
        {
            throw new DomainException(first.Message);
        }

        IssuerCountry = NormaliseCountry(issuerCountry);

        var stated = rows.ToDictionary(row => row.TaxYear);
        _fifTreatments.RemoveAll(row => !stated.ContainsKey(row.TaxYear));
        foreach (var row in rows)
        {
            var note = NormaliseNote(row.Note);
            if (_fifTreatments.Find(existing => existing.TaxYear == row.TaxYear) is { } existing)
            {
                existing.Set(row.Treatment, note);
            }
            else
            {
                _fifTreatments.Add(InstrumentFifTreatment.Create(TenantId, row.TaxYear, row.Treatment, note));
            }
        }

        _fifTreatments.Sort((left, right) => left.TaxYear.CompareTo(right.TaxYear));
    }

    /// <summary>
    /// Instrument-tax-attributes R10, R11: the treatment of a tax year. The latest row not after the year stands for
    /// it, unless that row is an exemption for an earlier year. No such row → <see cref="FifTreatment.NotClassified"/>.
    /// </summary>
    public FifTreatment FifTreatmentFor(int taxYear) =>
        FifTreatmentRowFor(taxYear)?.Treatment ?? FifTreatment.NotClassified;

    /// <summary>R11, R28: the row <see cref="FifTreatmentFor"/> took its answer from, or none when not classified.</summary>
    public InstrumentFifTreatment? FifTreatmentRowFor(int taxYear)
    {
        var row = _fifTreatments.Where(candidate => candidate.TaxYear <= taxYear).MaxBy(candidate => candidate.TaxYear);
        if (row is null || (row.TaxYear != taxYear && row.Treatment.IsExemption()))
        {
            return null;
        }

        return row;
    }

    /// <summary>R5, R9, R12: every reason the tax attributes cannot be stored, keyed by field; empty when they can.</summary>
    public static IReadOnlyList<TaxAttributeRefusal> TaxAttributeRefusals(
        string? issuerCountry,
        IReadOnlyList<FifTreatmentInput> rows,
        int latestTaxYear)
    {
        var refusals = new List<TaxAttributeRefusal>();
        var country = NormaliseCountry(issuerCountry);
        var countryKnown = country is not null && Countries.IsKnown(country);
        if (country is not null && !countryKnown)
        {
            refusals.Add(new(nameof(IssuerCountry), "IssuerCountry must be an ISO 3166-1 alpha-2 country code."));
        }

        if (rows.Count > MaxFifTreatments)
        {
            refusals.Add(new(nameof(FifTreatments), $"At most {MaxFifTreatments} FIF treatments can be entered."));
        }

        if (rows.GroupBy(row => row.TaxYear).Any(group => group.Count() > 1))
        {
            refusals.Add(new(nameof(FifTreatments), "Each tax year can be entered once."));
        }

        if (rows.Count > 0 && country is null)
        {
            refusals.Add(new(nameof(IssuerCountry), "IssuerCountry is required when a FIF treatment is set."));
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var prefix = $"{nameof(FifTreatments)}[{index}].";
            if (row.TaxYear < FirstTaxYear || row.TaxYear > latestTaxYear)
            {
                refusals.Add(new(prefix + nameof(FifTreatmentInput.TaxYear),
                    $"TaxYear must be between {FirstTaxYear} and {latestTaxYear}."));
            }

            if (!row.Treatment.IsStored())
            {
                refusals.Add(new(prefix + nameof(FifTreatmentInput.Treatment),
                    "Treatment must be NotForeign, ExemptAsxListed, ExemptOther, Attributing, or AttributingNoFdr."));
            }
            else if (countryKnown && AgreementRefusal(country!, row.Treatment) is { } disagreement)
            {
                refusals.Add(new(prefix + nameof(FifTreatmentInput.Treatment), disagreement));
            }

            var note = NormaliseNote(row.Note);
            if (note is { Length: > MaxNoteLength })
            {
                refusals.Add(new(prefix + nameof(FifTreatmentInput.Note), $"Note must be at most {MaxNoteLength} characters."));
            }
            else if (note is null && row.Treatment == FifTreatment.ExemptOther)
            {
                refusals.Add(new(prefix + nameof(FifTreatmentInput.Note), "Note is required for ExemptOther: name the exemption."));
            }
        }

        return refusals;
    }

    /// <summary>R9: the first tax year a row may be for, a guard against typing errors.</summary>
    public const int FirstTaxYear = 2008;

    /// <summary>R9: the most rows an Instrument may have.</summary>
    public const int MaxFifTreatments = 50;

    /// <summary>R9: the longest note of a row.</summary>
    public const int MaxNoteLength = 500;

    /// <summary>R12: the treatments the issuer country allows.</summary>
    private static string? AgreementRefusal(string country, FifTreatment treatment)
    {
        if (country == Countries.NewZealand)
        {
            return treatment == FifTreatment.NotForeign ? null : "An issuer resident in New Zealand is NotForeign.";
        }

        return treatment switch
        {
            FifTreatment.NotForeign => "NotForeign is for an issuer resident in New Zealand.",
            FifTreatment.ExemptAsxListed when country != Countries.Australia => "ExemptAsxListed is for an issuer resident in Australia.",
            _ => null
        };
    }

    private static string? NormaliseCountry(string? country) => country?.Trim().ToUpperInvariant();

    private static string? NormaliseNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

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

        if (!currency.IsActive)
        {
            throw new DomainException("Quote currency must be enabled.");
        }
    }
}
