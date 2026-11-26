using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Entities;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.Valuations;

/// <summary>
/// The worked example of account-valuation §1.1 on an External NZD account, built through the API with the caller's
/// <c>bookedAt</c>. Three differences from the Custodial account of the example, none of which changes a figure:
/// the opening balance is a <c>TransferIn</c> and an <c>OpeningHoldings</c>; the fee of T3 is a <c>TransferOut</c>
/// at the same instant, because an External fee is entered in the quote currency; and T5 is a hand <c>Split</c>
/// booked on its ex-date, as a caller books one (R19 (b)). T4 is a Reversal, whose <c>bookedAt</c> the server sets,
/// so it is moved in the database.
/// </summary>
internal sealed record ValuationWorld(
    Tenant Tenant,
    User Adviser,
    User Customer,
    string Token,
    Guid AccountId,
    Guid Aaa,
    Guid Bbb,
    Guid Ccc,
    Guid T2)
{
    public static DateOnly Day(int day) => new(2026, 3, day);

    public static async Task<ValuationWorld> CreateAsync()
    {
        var (tenant, adviser, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var token = tokens.AccessToken;
        var accountId = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var aaa = await CustodialHttp.CreateInstrumentAsync(token, "AAA", "NZD", price: null);
        var bbb = await CustodialHttp.CreateInstrumentAsync(token, "BBB", "USD", price: null);
        var ccc = await CustodialHttp.CreateInstrumentAsync(token, "CCC", "USD", price: null);

        await PostAsync(token, Cash(accountId, "TransferIn", 10_000.00m, "2026-02-27T09:00:00Z"));
        await PostAsync(token, Opening(accountId, "2026-02-27T09:00:00Z", (aaa, 1_000m, 2_400.00m), (bbb, 50m, 8_500.00m), (ccc, 200m, 7_600.00m)));
        // T1 at 09:30 on Wednesday in Auckland: Tuesday 20:30 UTC.
        await PostAsync(token, Trade(accountId, "Buy", aaa, 500m, 2.50m, "2026-03-04T09:30:00+13:00", fee: 15.00m));
        var t2 = await PostAsync(token, Cash(accountId, "TransferIn", 2_000.00m, "2026-03-04T02:00:00Z"));
        await PostAsync(token, Trade(accountId, "Sell", ccc, 80m, 41.00m, "2026-03-05T22:10:00Z", fxRate: 1.7200m));
        await PostAsync(token, Cash(accountId, "TransferOut", -20.00m, "2026-03-05T22:10:00Z"));
        var t4 = await CustodialHttp.ReadIdAsync(await CustodialHttp.ReverseAsync(token, t2));
        await SetBookedAtAsync(t4, new DateTimeOffset(2026, 3, 6, 1, 15, 0, TimeSpan.Zero));
        await PostAsync(token, Split(accountId, bbb, 100m, "2026-03-05T12:00:00Z"));

        foreach (var (day, price) in new[] { (2, 2.48m), (3, 2.50m), (4, 2.52m), (5, 2.47m), (6, 2.55m), (9, 2.60m), (10, 2.58m), (11, 2.61m) })
        {
            await AddPriceAsync(aaa, "NZD", Day(day), price);
        }

        foreach (var (day, price) in new[] { (2, 180.00m), (3, 182.00m), (4, 184.00m), (5, 92.50m), (6, 93.10m), (9, 94.00m), (10, 93.40m), (11, 95.20m) })
        {
            await AddPriceAsync(bbb, "USD", Day(day), price);
        }

        foreach (var (day, price) in new[] { (2, 40.00m), (3, 40.50m), (4, 40.20m), (5, 41.00m), (6, 41.30m) })
        {
            await AddPriceAsync(ccc, "USD", Day(day), price);
        }

        foreach (var (day, rate) in new[]
                 {
                     (2, 1.7100m), (3, 1.7150m), (4, 1.7180m), (5, 1.7200m), (6, 1.7250m), (7, 1.7250m), (8, 1.7260m),
                     (9, 1.7300m), (10, 1.7280m), (11, 1.7310m)
                 })
        {
            await AddRateAsync("USD", "NZD", Day(day), rate);
        }

        return new ValuationWorld(tenant, adviser, customer, token, accountId, aaa, bbb, ccc, t2);
    }
}
