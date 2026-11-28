using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Transactions;
using static MyWealthV2.Application.FunctionalTests.TaxYears.TaxYearHttp;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.TaxYears;

/// <summary>
/// The worked example of income-summary §1.1 through the API: the world of tax-year-events §1.1 with KIW and OZB, the
/// hand-entered I1, I2, D1 and D2 on the External NZD account A2, and the interest run's postings I3, I4 and I5 on the
/// Custodial USD account A1, written with the Domain factory (an interest run posts only for today).
/// </summary>
internal sealed record IncomeSummaryWorld(TaxYearWorld Base, Guid Kiw, Guid Ozb, IReadOnlyDictionary<string, Guid> P)
{
    public string Token => Base.Token;

    public Guid CustomerId => Base.Customer.PublicId;

    public static async Task<IncomeSummaryWorld> CreateAsync(
        string tenantName = "North Advisory", string tenantCode = "north-advisory", string domain = "north.example")
    {
        var world = await TaxYearWorld.CreateAsync(tenantName, tenantCode, domain);
        var token = world.Token;
        var kiw = await CustodialHttp.CreateInstrumentAsync(token, "KIW", "NZD", price: null);
        await ClassifyAsync(token, kiw, "NZ", 2025, "NotForeign");
        var ozb = await CustodialHttp.CreateInstrumentAsync(token, "OZB", "AUD", price: null);
        await ClassifyAsync(token, ozb, "AU", 2026, "ExemptAsxListed");

        var p = new Dictionary<string, Guid>
        {
            ["I1"] = await HandInterestAsync(token, world.A2, 120.00m, "2025-09-30T00:00:00Z", ("Rwt", 17.5m, 21.00m)),
            ["I2"] = await HandInterestAsync(token, world.A2, -8.00m, "2025-10-15T00:00:00Z"),
            ["D1"] = await HandDividendAsync(token, world.A2, kiw, 1_000m, 720.00m, "NewZealand", "2025-12-12T00:00:00Z",
                ExternalDividendTests.Taxes(("Rwt", 33m, 50.00m)), imputationCredit: 280.00m),
            ["I3"] = await PostInterestAsync(world.A1, 4.10m, 33m, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), Utc(2026, 2, 1, 0, 30)),
            ["I4"] = await PostInterestAsync(world.A1, 3.80m, 33m, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), Utc(2026, 3, 1, 0, 30)),
            ["D2"] = await HandDividendAsync(token, world.A2, ozb, 200m, 300.00m, "Foreign", "2026-03-20T00:00:00Z",
                ExternalDividendTests.Taxes(("ForeignWithholding", 15m, 45.00m)), fxRate: 1.0900m),
            ["I5"] = await PostInterestAsync(world.A1, 4.30m, 33m, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), Utc(2026, 4, 1, 0, 30))
        };
        await AddRateAsync("USD", "NZD", new DateOnly(2026, 1, 30), 1.7200m);
        await AddRateAsync("USD", "NZD", new DateOnly(2026, 2, 27), 1.7500m);
        return new IncomeSummaryWorld(world, kiw, ozb, p);
    }

    public static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>A hand-entered <c>Interest</c> on an External account, with its taxes (external-income R22–R24).</summary>
    public static async Task<Guid> HandInterestAsync(
        string token, Guid accountId, decimal amount, string bookedAt, params (string Kind, decimal RatePercent, decimal Amount)[] taxes)
    {
        var body = CustodialHttp.Cash(accountId, "Interest", amount).WithBookedAt(bookedAt);
        if (taxes.Length > 0)
        {
            body["taxes"] = ExternalDividendTests.Taxes(taxes);
        }

        return await CustodialHttp.ReadIdAsync(await CustodialHttp.PostAsync(token, body));
    }

    /// <summary>A hand-entered <c>Dividend</c> on an External account (external-income R11–R14).</summary>
    public static async Task<Guid> HandDividendAsync(
        string token,
        Guid accountId,
        Guid instrumentId,
        decimal shares,
        decimal gross,
        string taxSource,
        string bookedAt,
        object[] taxes,
        decimal? imputationCredit = null,
        decimal? fxRate = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["instrumentId"] = instrumentId,
            ["quantity"] = shares,
            ["grossAmount"] = gross,
            ["taxSource"] = taxSource,
            ["bookedAt"] = bookedAt,
            ["taxes"] = taxes
        };
        if (imputationCredit is { } credit)
        {
            body["imputationCredit"] = credit;
        }

        if (fxRate is { } rate)
        {
            body["fxRate"] = rate;
        }

        return await CustodialHttp.ReadIdAsync(await ExternalDividendTests.PostAsync(token, body));
    }
}
