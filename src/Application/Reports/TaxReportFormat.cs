using System.Globalization;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.Reports;

/// <summary>
/// How a report of one tax year prints a figure, an instant, a date, a country, a treatment and a status
/// (docs/features/fif-report.md R13, R14, §4.6; docs/features/income-summary.md R23, §4.6). The FIF report and the
/// Tax Year Income Summary print them alike.
/// </summary>
internal static class TaxReportFormat
{
    public const string NotAvailable = "not available";

    /// <summary>R14: a quantity or a rate as the read gives it, with up to 8 decimal places and no trailing zeros.</summary>
    public static string Number(decimal value) =>
        Math.Round(value, 8, MidpointRounding.AwayFromZero).ToString("0.########", CultureInfo.InvariantCulture);

    public static string Signed(decimal value) => value > 0m ? "+" + Number(value) : Number(value);

    /// <summary>R13: an instant in New Zealand time, or in UTC with "UTC" after it before the rule of 2007.</summary>
    public static string Time(DateTimeOffset instant) =>
        NewZealandTime.Local(instant) is { } local
            ? local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : UtcMinute(instant) + " UTC";

    /// <summary>R15: "{New Zealand time} New Zealand time ({UTC} UTC)".</summary>
    public static string Instant(DateTimeOffset instant) =>
        $"{Time(instant)} New Zealand time ({instant.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC)";

    public static string UtcMinute(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string ResidencyLabel(TaxResidency residency) =>
        residency == TaxResidency.NewZealand ? "New Zealand" : "Overseas";

    /// <summary>R18: the issuer country by its name in the list of instrument-tax-attributes R6.</summary>
    public static string? CountryName(string? code) =>
        code is null ? null : Domain.Instruments.Countries.All.FirstOrDefault(country => country.Code == code)?.Name ?? code;

    /// <summary>§4.6: the label of a treatment.</summary>
    public static string TreatmentLabel(TaxTreatment treatment) => treatment.FifTreatment switch
    {
        FifTreatment.Attributing => "Attributing interest",
        FifTreatment.AttributingNoFdr => "Attributing interest, fair dividend rate not allowed",
        FifTreatment.ExemptAsxListed => "Exempt: ASX-listed Australian share",
        FifTreatment.ExemptOther => $"Exempt: {treatment.Note}",
        FifTreatment.NotForeign => "Not foreign (New Zealand)",
        _ => "Not classified"
    };

    /// <summary>§4.6: the label of a status that leaves a figure missing.</summary>
    public static string StatusLabel(ValuationLineStatus status) => status switch
    {
        ValuationLineStatus.NoRate => "No exchange rate",
        ValuationLineStatus.StaleRate => "Exchange rate too old",
        ValuationLineStatus.NoPrice => "No price",
        ValuationLineStatus.StalePrice => "Price too old",
        ValuationLineStatus.PriceBeforeCorporateAction => "Price from before a corporate action",
        ValuationLineStatus.NegativeQuantity => "Quantity below 0",
        _ => ""
    };

    /// <summary>§4.6: the label of the status of an event's amount, when it is missing.</summary>
    public static string StatusLabel(TaxAmountStatus status) => status switch
    {
        TaxAmountStatus.NoRate => "No exchange rate",
        TaxAmountStatus.StaleRate => "Exchange rate too old",
        TaxAmountStatus.NoPrice => "No price",
        TaxAmountStatus.StalePrice => "Price too old",
        _ => ""
    };

    /// <summary>A rate row as "x {rate} ({date})".</summary>
    public static string RateText(StoredRate rate) => $"x {Number(rate.Rate)} ({Date(rate.AsOfDate)})";

    /// <summary>Custody-mode-grouping R14.</summary>
    public static string CustodyLabel(CustodyMode mode) =>
        mode == CustodyMode.Custodial ? "Held on platform" : "Held elsewhere";
}
