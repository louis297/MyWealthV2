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

    public static IReadOnlyList<string> ReportCodes { get; } =
        [CustomerTransactionsReportCode, AdviserBalancesReportCode];

    public static IReadOnlyList<DemoInstrument> Instruments { get; } =
    [
        new("VTI", "Vanguard Total Stock Market ETF", "USD"),
        new("AAPL", "Apple Inc.", "USD"),
        new("MSFT", "Microsoft Corporation", "USD"),
        new("AIA", "Auckland International Airport", "NZD"),
        new("FPH", "Fisher & Paykel Healthcare", "NZD"),
        new("MEL", "Meridian Energy", "NZD"),
        new("BHP", "BHP Group", "AUD"),
        new("CBA", "Commonwealth Bank of Australia", "AUD"),
        new("WOW", "Woolworths Group", "AUD"),
        new("SAP", "SAP SE", "EUR"),
        new("AIR", "Airbus SE", "EUR"),
        new("OR", "L'Oréal", "EUR"),
        new("SHEL", "Shell plc", "GBP"),
        new("HSBA", "HSBC Holdings", "GBP"),
        new("ULVR", "Unilever", "GBP"),
        new("7203", "Toyota Motor", "JPY"),
        new("6758", "Sony Group", "JPY"),
        new("9984", "SoftBank Group", "JPY")
    ];
}

public sealed record DemoInstrument(string Symbol, string Name, string QuoteCurrency);
