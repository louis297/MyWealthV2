using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Valuations;
using MyWealthV2.Domain.Entities;
using static MyWealthV2.Application.FunctionalTests.TaxYears.TaxYearHttp;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.TaxYears;

/// <summary>
/// The worked example of tax-year-events §1.1 through the API. A2 is an External NZD account and takes the caller's
/// <c>bookedAt</c>. A1 is opened as an External USD account so that its trades, split and stock in can be dated, and is
/// made Custodial after them, as the valuation tests do. Its two dividends are posted with the Domain factory, because a
/// reinvested dividend is never entered by hand. T6 is a Reversal, whose <c>bookedAt</c> the server sets, so it is moved
/// in the database.
/// </summary>
internal sealed record TaxYearWorld(
    Tenant Tenant,
    User Adviser,
    User Customer,
    string Token,
    Guid A1,
    Guid A2,
    Guid Aaa,
    IReadOnlyDictionary<string, Guid> T)
{
    public static async Task<TaxYearWorld> CreateAsync(
        string tenantName = "North Advisory", string tenantCode = "north-advisory", string domain = "north.example")
    {
        var (tenant, adviser, customer, tokens) = await AccountHttp.SignInTenantAdmin(
            tenantName, tenantCode, $"tess@{domain}", adviserEmail: $"ann@{domain}", customerEmail: $"ned@{domain}");
        var token = tokens.AccessToken;
        var a1 = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Managed", "Brokerage", "USD");
        var a2 = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var aaa = await CustodialHttp.CreateInstrumentAsync(token, "AAA", "USD", price: null);
        await ClassifyAsync(token, aaa, "US", 2025, "Attributing");

        await PostAsync(token, Cash(a1, "TransferIn", 10_000.00m, "2025-03-01T00:00:00Z"));
        await PostAsync(token, Cash(a2, "TransferIn", 60_000.00m, "2025-03-01T00:00:00Z"));
        var t = new Dictionary<string, Guid>
        {
            ["T1"] = await PostAsync(token, Trade(a1, "Buy", aaa, 100m, 50.00m, "2025-03-10T21:00:00Z", fee: 5.00m)),
            ["T2"] = await PostAsync(token, Trade(a2, "Buy", aaa, 40m, 52.00m, "2025-03-31T09:30:00Z", fee: 4.00m, fxRate: 1.7500m)),
            ["T3"] = await PostAsync(token, Trade(a1, "Sell", aaa, 30m, 53.00m, "2025-03-31T20:15:00Z", fee: 5.00m)),
            ["T5"] = await PostAsync(token, Trade(a2, "Buy", aaa, 500m, 55.00m, "2025-06-03T04:00:00Z", fxRate: 1.7500m))
        };
        t["T6"] = await CustodialHttp.ReadIdAsync(await CustodialHttp.ReverseAsync(token, t["T5"]));
        await SetBookedAtAsync(t["T6"], new DateTimeOffset(2025, 6, 5, 1, 0, 0, TimeSpan.Zero));
        t["T7"] = await PostAsync(token, Split(a2, aaa, 80m, "2025-07-01T00:00:00Z"));
        t["T8"] = await PostAsync(token, Split(a1, aaa, 140m, "2025-07-02T12:01:00Z"));
        t["T10"] = await PostAsync(token, Security(a2, "StockOut", aaa, 80m, "2025-11-10T00:00:00Z"));
        t["T11"] = await PostAsync(token, Security(a1, "StockIn", aaa, 80m, "2025-11-14T03:20:00Z", cost: 4_169.00m));
        t["T12"] = await PostAsync(token, Trade(a1, "Buy", aaa, 20m, 30.00m, "2026-02-02T21:40:00Z", fee: 5.00m));
        await ValuationRunDb.MakeCustodialAsync(a1, new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero));
        t["T4"] = await PostDividendAsync(a1, aaa, 70m, 0.50m, new DateTimeOffset(2025, 5, 20, 2, 10, 0, TimeSpan.Zero));
        t["T9"] = await PostDividendAsync(a1, aaa, 140m, 0.25m, new DateTimeOffset(2025, 9, 15, 2, 30, 0, TimeSpan.Zero), 28.00m, 0.8m);

        await AddPriceAsync(aaa, "USD", new DateOnly(2025, 3, 31), 53.20m);
        await AddPriceAsync(aaa, "USD", new DateOnly(2026, 3, 31), 31.00m);
        foreach (var (date, rate) in new[]
                 {
                     (new DateOnly(2025, 3, 10), 1.7400m), (new DateOnly(2025, 3, 31), 1.7600m), (new DateOnly(2025, 5, 20), 1.6900m),
                     (new DateOnly(2025, 9, 15), 1.6800m), (new DateOnly(2026, 2, 2), 1.7300m), (new DateOnly(2026, 3, 31), 1.7450m)
                 })
        {
            await AddRateAsync("USD", "NZD", date, rate);
        }

        return new TaxYearWorld(tenant, adviser, customer, token, a1, a2, aaa, t);
    }
}
