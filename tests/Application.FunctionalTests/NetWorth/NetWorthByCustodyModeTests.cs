using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.NetWorth;

/// <summary>The <c>byCustodyMode</c> breakdown of net worth (docs/features/custody-mode-grouping.md R4–R9).</summary>
[Parallelizable]
public class NetWorthByCustodyModeTests : TestBase
{
    [Test]
    public async Task Firm_SplitsCustodialAndExternalAccounts_AndTheEntriesAddUpToTheTotals()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var custodial = await CustodialHttp.OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var external = await CustodialHttp.OpenExternalAsync(tokens.AccessToken, customer.PublicId);
        var bank = await CustodialHttp.OpenExternalAsync(tokens.AccessToken, customer.PublicId, "Everyday", "Bank", "NZD");

        (await CustodialHttp.FundAsync(tokens.AccessToken, custodial, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CustodialHttp.FundAsync(tokens.AccessToken, external, 300m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CustodialHttp.FundAsync(tokens.AccessToken, bank, 50m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var vti = await CustodialHttp.CreateInstrumentAsync(tokens.AccessToken);
        (await CustodialHttp.PostAsync(tokens.AccessToken, CustodialHttp.Trade(custodial, "Buy", vti, 4m)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        using var json = await ReadAsync("/net-worth", tokens.AccessToken);
        var root = json.RootElement;
        root.GetProperty("breakdowns").EnumerateObject().Select(property => property.Name)
            .ShouldBe(["byCustomer", "byAccountType", "byCustodyMode", "topHoldings"]);

        var entries = Entries(root);
        entries.Select(entry => entry.GetProperty("custodyMode").GetString()).ShouldBe(["Custodial", "External"]);
        entries[0].EnumerateObject().Select(property => property.Name).ShouldBe(["custodyMode", "currencies"]);

        var custodialCurrencies = entries[0].GetProperty("currencies").EnumerateArray().ToList();
        custodialCurrencies.Count.ShouldBe(1);
        AssertCurrency(custodialCurrencies[0], "USD", cash: 900m, holdings: 100m);

        var externalCurrencies = entries[1].GetProperty("currencies").EnumerateArray().ToList();
        externalCurrencies.Select(row => row.GetProperty("currency").GetString()).ShouldBe(["NZD", "USD"]);
        AssertCurrency(externalCurrencies[0], "NZD", cash: 50m, holdings: 0m);
        AssertCurrency(externalCurrencies[1], "USD", cash: 300m, holdings: 0m);

        AssertPartition(root);
    }

    [Test]
    public async Task ExternalOnly_HasOneEntry_AnEmptyAccountCounts_AndAClosedCustodialAccountDoesNot()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        await CustodialHttp.OpenExternalAsync(tokens.AccessToken, customer.PublicId, "Empty tin", "Cash", "GBP");
        var closed = await CustodialHttp.OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Old managed");
        (await CustodialHttp.CloseAsync(tokens.AccessToken, closed)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var json = await ReadAsync("/net-worth", tokens.AccessToken);
        var entries = Entries(json.RootElement);
        entries.Select(entry => entry.GetProperty("custodyMode").GetString()).ShouldBe(["External"]);
        var currencies = entries[0].GetProperty("currencies").EnumerateArray().ToList();
        currencies.Count.ShouldBe(1);
        AssertCurrency(currencies[0], "GBP", cash: 0m, holdings: 0m);
        AssertPartition(json.RootElement);
    }

    [Test]
    public async Task NoAccounts_IsAnEmptyArray()
    {
        var (_, _, _, tokens) = await AccountHttp.SignInAdviser();

        using var json = await ReadAsync("/net-worth", tokens.AccessToken);
        Entries(json.RootElement).ShouldBeEmpty();
    }

    [Test]
    public async Task Scope_AdviserCustomerAndOtherTenantAccountsStayOut()
    {
        var (tenant, adviser, customer, tokens) = await AccountHttp.SignInAdviser();
        var own = await CustodialHttp.OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        (await CustodialHttp.FundAsync(tokens.AccessToken, own, 70m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // A second Customer of the same Adviser, and one of another Adviser in the same firm.
        var sibling = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Sid Sibling", "sid@north.example", "Password1!", tenant.Id, adviser.Id);
        var siblingAccount = await CustodialHttp.OpenCustodialAsync(tokens.AccessToken, sibling.PublicId);
        (await CustodialHttp.FundAsync(tokens.AccessToken, siblingAccount, 5m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var pat = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, otherAdviser.Id);
        var blairTokens = await FunctionalTestSetup.Oidc.SignInAsync("blair@north.example", "Password1!", "north-advisory");
        var patAccount = await CustodialHttp.OpenCustodialAsync(blairTokens.AccessToken, pat.PublicId);
        (await CustodialHttp.FundAsync(blairTokens.AccessToken, patAccount, 999m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var (_, _, southCustomer, southTokens) = await AccountHttp.SignInAdviser(
            "South Co", "south-co", "ann@south.example", customerEmail: "ned@south.example");
        var southAccount = await CustodialHttp.OpenCustodialAsync(southTokens.AccessToken, southCustomer.PublicId);
        (await CustodialHttp.FundAsync(southTokens.AccessToken, southAccount, 4000m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        using (var firm = await ReadAsync("/net-worth", tokens.AccessToken))
        {
            var entries = Entries(firm.RootElement);
            entries.Select(entry => entry.GetProperty("custodyMode").GetString()).ShouldBe(["Custodial"]);
            AssertCurrency(entries[0].GetProperty("currencies")[0], "USD", cash: 75m, holdings: 0m);
        }

        using (var one = await ReadAsync($"/users/customers/{customer.PublicId}/net-worth", tokens.AccessToken))
        {
            var entries = Entries(one.RootElement);
            entries.Count.ShouldBe(1);
            AssertCurrency(entries[0].GetProperty("currencies")[0], "USD", cash: 70m, holdings: 0m);
        }

        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);
        using (var self = await ReadAsync("/net-worth", customerTokens.AccessToken))
        {
            var entries = Entries(self.RootElement);
            entries.Count.ShouldBe(1);
            AssertCurrency(entries[0].GetProperty("currencies")[0], "USD", cash: 70m, holdings: 0m);
        }

        (await AccountHttp.Send(
            HttpMethod.Get, $"/users/customers/{southCustomer.PublicId}/net-worth", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<JsonDocument> ReadAsync(string path, string accessToken)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, path, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        return JsonDocument.Parse(content);
    }

    private static List<JsonElement> Entries(JsonElement root) =>
        root.GetProperty("breakdowns").GetProperty("byCustodyMode").EnumerateArray().ToList();

    /// <summary>Per currency, the entries add up to the top-level values (R6).</summary>
    private static void AssertPartition(JsonElement root)
    {
        var sums = new Dictionary<string, (decimal Cash, decimal Holdings, decimal Total, decimal Net)>();
        foreach (var currency in Entries(root).SelectMany(entry => entry.GetProperty("currencies").EnumerateArray()))
        {
            var code = currency.GetProperty("currency").GetString()!;
            var assets = currency.GetProperty("assets");
            var sum = sums.GetValueOrDefault(code);
            sums[code] = (
                sum.Cash + assets.GetProperty("cash").GetDecimal(),
                sum.Holdings + assets.GetProperty("holdings").GetDecimal(),
                sum.Total + assets.GetProperty("total").GetDecimal(),
                sum.Net + currency.GetProperty("net").GetDecimal());
        }

        var top = root.GetProperty("currencies").EnumerateArray().ToList();
        sums.Keys.Order(StringComparer.Ordinal).ShouldBe(top.Select(row => row.GetProperty("currency").GetString()!));
        foreach (var row in top)
        {
            var sum = sums[row.GetProperty("currency").GetString()!];
            var assets = row.GetProperty("assets");
            sum.Cash.ShouldBe(assets.GetProperty("cash").GetDecimal());
            sum.Holdings.ShouldBe(assets.GetProperty("holdings").GetDecimal());
            sum.Total.ShouldBe(assets.GetProperty("total").GetDecimal());
            sum.Net.ShouldBe(row.GetProperty("net").GetDecimal());
        }
    }

    private static void AssertCurrency(JsonElement row, string currency, decimal cash, decimal holdings)
    {
        row.EnumerateObject().Select(property => property.Name).ShouldBe(["currency", "assets", "liabilities", "net"]);
        row.GetProperty("currency").GetString().ShouldBe(currency);
        var assets = row.GetProperty("assets");
        assets.GetProperty("cash").GetDecimal().ShouldBe(cash);
        assets.GetProperty("holdings").GetDecimal().ShouldBe(holdings);
        assets.GetProperty("total").GetDecimal().ShouldBe(cash + holdings);
        row.GetProperty("liabilities").GetProperty("total").GetDecimal().ShouldBe(0m);
        row.GetProperty("net").GetDecimal().ShouldBe(cash + holdings);
    }
}
