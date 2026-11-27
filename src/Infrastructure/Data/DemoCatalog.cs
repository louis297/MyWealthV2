using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Instruments;

namespace MyWealthV2.Infrastructure.Data;

/// <summary>
/// The demo tenant and its demo catalog (demo-tenant-seed §5.1, R13). Shared by the demo seed
/// and the Development seeders, so both write the same tenant code, name, instruments and grants.
/// </summary>
public static class DemoCatalog
{
    public const string TenantCode = "demo";

    public const string TenantName = "Demo Advisory";

    public const string CustomerTransactionsReportCode = "customer-transactions";

    public const string AdviserBalancesReportCode = "adviser-balances";

    public const string BankReconciliationReportCode = "bank-reconciliation";

    public static IReadOnlyList<string> ReportCodes { get; } =
        [CustomerTransactionsReportCode, AdviserBalancesReportCode, BankReconciliationReportCode];

    /// <summary>Instrument-tax-attributes §5.1: the tax year of every row the seeds write.</summary>
    public const int TaxYear = 2027;

    /// <summary>
    /// Instrument-tax-attributes §5.1, §11 point 9: the note of the three ASX rows. Inland Revenue's tool answered for
    /// tax year 2026 only when a person checked it, so the 2027 rows are an assumption.
    /// </summary>
    public const string AsxNote =
        "Assumed from tax year 2026: on 2026-11-27 Inland Revenue's FIF Australian listed share exemption tool showed the exemption for 1 April 2025 to 31 March 2026 only.";

    public static IReadOnlyList<DemoInstrument> Instruments { get; } =
    [
        new("VTI", "Vanguard Total Stock Market ETF", "USD", "US", FifTreatment.Attributing),
        new("AAPL", "Apple Inc.", "USD", "US", FifTreatment.Attributing),
        new("MSFT", "Microsoft Corporation", "USD", "US", FifTreatment.Attributing),
        new("AIA", "Auckland International Airport", "NZD", "NZ", FifTreatment.NotForeign),
        new("FPH", "Fisher & Paykel Healthcare", "NZD", "NZ", FifTreatment.NotForeign),
        new("MEL", "Meridian Energy", "NZD", "NZ", FifTreatment.NotForeign),
        new("BHP", "BHP Group", "AUD", "AU", FifTreatment.ExemptAsxListed, AsxNote),
        new("CBA", "Commonwealth Bank of Australia", "AUD", "AU", FifTreatment.ExemptAsxListed, AsxNote),
        new("WOW", "Woolworths Group", "AUD", "AU", FifTreatment.ExemptAsxListed, AsxNote),
        new("SAP", "SAP SE", "EUR", "DE", FifTreatment.Attributing),
        new("AIR", "Airbus SE", "EUR", "NL", FifTreatment.Attributing),
        new("OR", "L'Oréal", "EUR", "FR", FifTreatment.Attributing),
        new("SHEL", "Shell plc", "GBP", "GB", FifTreatment.Attributing),
        new("HSBA", "HSBC Holdings", "GBP", "GB", FifTreatment.Attributing),
        new("ULVR", "Unilever", "GBP", "GB", FifTreatment.Attributing),
        new("7203", "Toyota Motor", "JPY", "JP", FifTreatment.Attributing),
        new("6758", "Sony Group", "JPY", "JP", FifTreatment.Attributing),
        new("9984", "SoftBank Group", "JPY", "JP", FifTreatment.Attributing)
    ];

    /// <summary>
    /// Instrument-tax-attributes R24, R25: writes the issuer country and the tax year 2027 row on an instrument that
    /// has no tax attribute at all, and returns true. An instrument with a country or a row is left unchanged.
    /// </summary>
    public static bool ClassifyIfUnclassified(
        Instrument instrument,
        string issuerCountry,
        FifTreatment treatment,
        string? note,
        int latestTaxYear,
        string createdBy)
    {
        if (instrument.IssuerCountry is not null || instrument.FifTreatments.Count > 0)
        {
            return false;
        }

        instrument.SetTaxAttributes(issuerCountry, [new FifTreatmentInput(TaxYear, treatment, note)], latestTaxYear);
        foreach (var row in instrument.FifTreatments)
        {
            row.CreatedBy ??= createdBy;
        }

        return true;
    }
}

/// <summary>One instrument of the demo catalog with its tax attributes for <see cref="DemoCatalog.TaxYear"/>.</summary>
public sealed record DemoInstrument(
    string Symbol,
    string Name,
    string QuoteCurrency,
    string IssuerCountry,
    FifTreatment Treatment,
    string? Note = null);
