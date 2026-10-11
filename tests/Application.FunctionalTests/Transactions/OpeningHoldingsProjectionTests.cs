using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

public class OpeningHoldingsProjectionTests : TestBase
{
    private const string BookedAt = "2026-09-27T09:00:00+12:00";

    [Test]
    public async Task Create_UpsertsOneHoldingPerInstrumentEqualToTheLegSums()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var singleAccount = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "One name");
        var vti = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");

        (await PostOpeningAsync(tokens.AccessToken, singleAccount, new[] { Leg(vti, 100m, 1500.00m) }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await LoadHoldingsAsync(singleAccount)).ShouldBe([
            (vti, 100m, 1500.00m, "USD")
        ]);

        var twoAccount = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Two names");
        var bnd = await CreateInstrumentAsync(tokens.AccessToken, "BND", "USD");

        (await PostOpeningAsync(
                tokens.AccessToken,
                twoAccount,
                new[] { Leg(bnd, 2m, 0m), Leg(vti, 40m, 600.50m) }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await LoadHoldingsAsync(twoAccount)).ShouldBe([
            (bnd, 2m, 0m, "USD"),
            (vti, 40m, 600.50m, "USD")
        ]);
    }

    private static async Task<List<(Guid InstrumentId, decimal Quantity, decimal Cost, string Currency)>> LoadHoldingsAsync(
        Guid accountPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await (
            from holding in db.Holdings.AsNoTracking()
            join account in db.Accounts.AsNoTracking() on holding.AccountId equals account.Id
            join instrument in db.Instruments.AsNoTracking() on holding.InstrumentId equals instrument.Id
            where account.PublicId == accountPublicId
            orderby instrument.Symbol
            select new
            {
                instrument.PublicId,
                holding.Quantity,
                holding.CostAmount,
                holding.CostCurrency
            }).ToListAsync();

        return rows
            .Select(row => (row.PublicId, row.Quantity, row.CostAmount, row.CostCurrency.Trim()))
            .ToList();
    }

    private static object Leg(Guid instrumentId, decimal quantity, decimal cost) =>
        new { instrumentId, quantity, cost };

    private static Task<HttpResponseMessage> PostOpeningAsync(
        string accessToken,
        Guid accountId,
        object security) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = "OpeningHoldings",
            bookedAt = BookedAt,
            security
        }, Guid.NewGuid());

    private static async Task<Guid> OpenAccountAsync(string accessToken, Guid customerId, string name)
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name,
            type = "Brokerage",
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateInstrumentAsync(string accessToken, string symbol, string quoteCurrency)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol,
            name = symbol + " fund",
            quoteCurrency
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
