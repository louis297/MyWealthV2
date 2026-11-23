using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Portfolios;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Transaction source R4, R5, R7, R8, R10: the <c>source</c> on the transaction item.</summary>
[Parallelizable]
public class TransactionSourceTests : TestBase
{
    private const string BookedAt = "2026-10-11T10:00:00Z";

    [TestCase("Adviser")]
    [TestCase("TenantAdmin")]
    [TestCase("SystemAdmin")]
    public async Task HandWrites_AreManualWithNoId_ForEveryCaller(string role)
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var token = role switch
        {
            "Adviser" => (await FunctionalTestSetup.Oidc.SignInAsync("ann@north.example", "Password1!", "north-advisory"))
                .AccessToken,
            "TenantAdmin" => admin.AccessToken,
            _ => (await AccountHttp.SignInSystemAdmin()).AccessToken
        };
        Guid? tenantId = role == "SystemAdmin" ? tenant.PublicId : null;
        var brokerage = await AccountHttp.OpenAccountAsync(
            admin.AccessToken, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var bank = await AccountHttp.OpenAccountAsync(admin.AccessToken, customer.PublicId, "Bank", "Bank", "NZD");
        var usd = await AccountHttp.OpenAccountAsync(admin.AccessToken, customer.PublicId, "USD bank", "Bank", "USD");
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(admin.AccessToken, "VTI", "NZD", price: null);

        var cash = await CreateAsync(token, "/transactions", tenantId, new()
        {
            ["accountId"] = brokerage,
            ["type"] = "TransferIn",
            ["amount"] = 1000m,
            ["currency"] = "NZD",
            ["bookedAt"] = BookedAt
        });
        var buy = await CreateAsync(token, "/transactions", tenantId, new()
        {
            ["accountId"] = brokerage,
            ["type"] = "Buy",
            ["bookedAt"] = BookedAt,
            ["security"] = new object[] { new { instrumentId, quantity = 1m, price = 10m } }
        });
        var transfer = await CreateAsync(token, "/transactions/transfer", tenantId, new()
        {
            ["fromAccountId"] = brokerage,
            ["toAccountId"] = bank,
            ["amount"] = 10m,
            ["bookedAt"] = BookedAt
        });
        var fx = await CreateAsync(token, "/transactions/fx", tenantId, new()
        {
            ["fromAccountId"] = brokerage,
            ["toAccountId"] = usd,
            ["sellAmount"] = 100m,
            ["buyAmount"] = 59m,
            ["bookedAt"] = BookedAt
        });
        var reversal = await CustodialHttp.ReadIdAsync(await CustodialHttp.ReverseAsync(token, transfer));

        foreach (var id in new[] { cash, buy, transfer, fx, reversal })
        {
            using var item = await CustodialHttp.GetTransactionAsync(admin.AccessToken, id);
            ShouldBeManual(item.RootElement);
        }

        var list = await ListAsync(admin.AccessToken, brokerage);
        list.Count.ShouldBe(5);
        list.ForEach(ShouldBeManual);
    }

    [Test]
    public async Task SourceInTheBody_IsIgnored()
    {
        var (_, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var bank = await AccountHttp.OpenAccountAsync(admin.AccessToken, customer.PublicId, "Bank", "Bank", "NZD");

        var id = await CreateAsync(admin.AccessToken, "/transactions", null, new()
        {
            ["accountId"] = bank,
            ["type"] = "TransferIn",
            ["amount"] = 100m,
            ["currency"] = "NZD",
            ["bookedAt"] = BookedAt,
            ["source"] = new { kind = "Seed", id = Guid.NewGuid() }
        });

        using var item = await CustodialHttp.GetTransactionAsync(admin.AccessToken, id);
        ShouldBeManual(item.RootElement);
    }

    [Test]
    public async Task RebalanceTrades_AreOrderFills_AndTheAccountRouteWritesNoTransaction()
    {
        var world = await RebalanceTrades.AssignedAsync();
        var assigned = await RebalanceTrades.TradesAsync(world.AccessToken, world.AccountId);
        (await RebalanceTrades.SplitHalfAndHalfAsync(world)).Dispose();
        // Rebalances R8: a proposal needs prices from the last 4 days.
        await Orders.OrderHttp.SetPriceAsync(world.First, 10m, Orders.OrderHttp.Today);
        await Orders.OrderHttp.SetPriceAsync(world.Second, 20m, Orders.OrderHttp.Today);

        // Rebalances R10, R26: the account route proposes, and no Transaction gets source Rebalance.
        var response = await PortfolioHttp.Send(
            HttpMethod.Post, $"/accounts/{world.AccountId}/rebalance", world.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        assigned.ShouldNotBeEmpty();
        assigned.ShouldAllBe(trade => Source(trade).GetProperty("kind").GetString() == "OrderFill");
        (await RebalanceTrades.TradesAsync(world.AccessToken, world.AccountId)).Count.ShouldBe(assigned.Count);
    }

    [Test]
    public async Task Customer_ReadsTheSourceOnTheirOwnTransactions()
    {
        var world = await RebalanceTrades.AssignedAsync();
        var customerTokens = await TestApp.IssueAccessTokenAsync(world.Customer);

        var items = await ListAsync(customerTokens.AccessToken, world.AccountId);

        items.Select(item => Source(item).GetProperty("kind").GetString()).Distinct()
            .ShouldBe(["BankFeed", "OrderFill"], ignoreOrder: true);
        var trade = items.First(item => item.GetProperty("type").GetString() == "Buy");
        using var single = await CustodialHttp.GetTransactionAsync(
            customerTokens.AccessToken, trade.GetProperty("id").GetGuid());
        Source(single.RootElement).GetProperty("kind").GetString().ShouldBe("OrderFill");
        single.RootElement.GetProperty("source").GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task AnotherTenantsTransaction_Returns404()
    {
        var other = await RebalanceTrades.AssignedAsync(
            "Other Firm", "other-firm", "adviser@other-firm.example", "customer@other-firm.example");
        var trade = (await RebalanceTrades.TradesAsync(other.AccessToken, other.AccountId)).First();
        var (_, _, _, tokens) = await AccountHttp.SignInAdviser();

        var response = await TransactionHttp.Send(
            HttpMethod.Get, $"/transactions/{trade.GetProperty("id").GetGuid()}", tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static void ShouldBeManual(JsonElement item)
    {
        var source = Source(item);
        source.GetProperty("kind").GetString().ShouldBe("Manual", item.ToString());
        source.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Null, item.ToString());
    }

    private static JsonElement Source(JsonElement item)
    {
        var source = item.GetProperty("source");
        source.ValueKind.ShouldBe(JsonValueKind.Object, item.ToString());
        return source;
    }

    private static async Task<Guid> CreateAsync(
        string accessToken,
        string path,
        Guid? tenantId,
        Dictionary<string, object?> body)
    {
        if (tenantId is not null)
        {
            body["tenantId"] = tenantId;
        }

        return await CustodialHttp.ReadIdAsync(await CustodialHttp.PostAsync(accessToken, body, path));
    }

    private static async Task<List<JsonElement>> ListAsync(string accessToken, Guid accountId)
    {
        var response = await TransactionHttp.Send(
            HttpMethod.Get, $"/transactions?accountId={accountId}&pageSize=100", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
    }
}
