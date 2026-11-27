using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Instruments;

public sealed class InstrumentDto
{
    public required Guid Id { get; init; }

    public required Guid TenantId { get; init; }

    public required string Symbol { get; init; }

    public required string Name { get; init; }

    public required string QuoteCurrency { get; init; }

    public required bool IsActive { get; init; }

    public required string RowVersion { get; init; }

    /// <summary>Simulated instruments R6: the connection that serves the symbol, or <c>null</c>.</summary>
    public string? MarketDataConnection { get; init; }

    /// <summary>Instrument-tax-attributes R21: the issuer country, or <c>null</c>.</summary>
    public string? IssuerCountry { get; init; }

    /// <summary>Instrument-tax-attributes R21: every FIF treatment row, sorted by tax year.</summary>
    public IReadOnlyList<FifTreatmentDto> FifTreatments { get; init; } = [];

    public static InstrumentDto From(Instrument instrument, Tenant tenant, string? marketDataConnection = null) => new()
    {
        Id = instrument.PublicId,
        TenantId = tenant.PublicId,
        Symbol = instrument.Symbol,
        Name = instrument.Name,
        QuoteCurrency = instrument.QuoteCurrency,
        IsActive = instrument.IsActive,
        RowVersion = Convert.ToBase64String(instrument.RowVersion ?? []),
        MarketDataConnection = marketDataConnection,
        IssuerCountry = instrument.IssuerCountry,
        FifTreatments = instrument.FifTreatments
            .OrderBy(row => row.TaxYear)
            .Select(row => new FifTreatmentDto(row.TaxYear, row.Treatment.ToString(), row.Note))
            .ToList()
    };
}

/// <summary>One FIF treatment row of the instrument item: the treatment by name.</summary>
public sealed record FifTreatmentDto(int TaxYear, string Treatment, string? Note);
