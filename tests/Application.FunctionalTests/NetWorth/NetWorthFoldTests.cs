using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Valuations;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.NetWorth;

/// <summary>Valuation-history R14, R15 and §1.1: <c>folded</c> on the two net-worth reads, at today's rates.</summary>
[Parallelizable]
public class NetWorthFoldTests : TestBase
{
    [Test]
    public async Task TheWorkedExample_FoldsTheNetOfEachCurrency_AtTodaysRate_OnBothReads()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        await FundedAsync(tokens.AccessToken, customer.PublicId, "NZD", 8_500.00m);
        await FundedAsync(tokens.AccessToken, customer.PublicId, "USD", 800.00m);
        await AddRateAsync("USD", "NZD", Today, 1.7320m);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        foreach (var (token, path) in new[]
                 {
                     (tokens.AccessToken, "/net-worth"),
                     (customerTokens.AccessToken, "/net-worth"),
                     (tokens.AccessToken, $"/users/customers/{customer.PublicId}/net-worth")
                 })
        {
            var folded = (await ValuationRunHttp.GetAsync(token, path)).Folded();

            folded.GetProperty("currency").GetString().ShouldBe("NZD");
            folded.GetProperty("total").GetDecimal().ShouldBe(9_885.60m);
            folded.GetProperty("complete").GetBoolean().ShouldBeTrue();
            var lines = folded.GetProperty("currencies").EnumerateArray().ToList();
            lines.Count.ShouldBe(2);
            AssertLine(lines[0], "NZD", 8_500.00m, "Folded", null, 8_500.00m);
            AssertLine(lines[1], "USD", 800.00m, "Folded", (1.732m, Today), 1_385.60m);
        }
    }

    [Test]
    public async Task ACurrencyWithNoRate_IsLeftOut_AndTheFoldIsIncomplete()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        await FundedAsync(tokens.AccessToken, customer.PublicId, "NZD", 100.00m);
        await FundedAsync(tokens.AccessToken, customer.PublicId, "GBP", 50.00m);

        var folded = (await ValuationRunHttp.GetAsync(tokens.AccessToken, "/net-worth")).Folded();

        folded.GetProperty("total").GetDecimal().ShouldBe(100.00m);
        folded.GetProperty("complete").GetBoolean().ShouldBeFalse();
        AssertLine(folded.GetProperty("currencies")[0], "GBP", 50.00m, "NoRate", null, null);
    }

    [Test]
    public async Task ARateMoreThanFourDaysOld_IsNotUsed()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        await FundedAsync(tokens.AccessToken, customer.PublicId, "AUD", 50.00m);
        await AddRateAsync("AUD", "NZD", Today.AddDays(-5), 1.1m);

        var folded = (await ValuationRunHttp.GetAsync(tokens.AccessToken, "/net-worth")).Folded();

        folded.GetProperty("total").GetDecimal().ShouldBe(0m);
        folded.GetProperty("complete").GetBoolean().ShouldBeFalse();
        AssertLine(folded.GetProperty("currencies")[0], "AUD", 50.00m, "StaleRate", (1.1m, Today.AddDays(-5)), null);
    }

    [Test]
    public async Task NoOpenAccount_FoldsToZero_AndIsComplete()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();

        foreach (var path in new[] { "/net-worth", $"/users/customers/{customer.PublicId}/net-worth" })
        {
            var folded = (await ValuationRunHttp.GetAsync(tokens.AccessToken, path)).Folded();

            folded.GetProperty("currency").GetString().ShouldBe("NZD");
            folded.GetProperty("total").GetDecimal().ShouldBe(0m);
            folded.GetProperty("complete").GetBoolean().ShouldBeTrue();
            folded.GetProperty("currencies").GetArrayLength().ShouldBe(0);
        }
    }

    [Test]
    public async Task TheBreakdownsAndThePerCurrencyTotals_StayPerCurrency()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        await FundedAsync(tokens.AccessToken, customer.PublicId, "NZD", 8_500.00m);
        await FundedAsync(tokens.AccessToken, customer.PublicId, "USD", 800.00m);
        await AddRateAsync("USD", "NZD", Today, 1.7320m);

        var netWorth = await ValuationRunHttp.GetAsync(tokens.AccessToken, "/net-worth");

        netWorth.GetProperty("currencies").EnumerateArray()
            .Select(row => (row.GetProperty("currency").GetString(), row.GetProperty("net").GetDecimal()))
            .ShouldBe([("NZD", 8_500.00m), ("USD", 800.00m)]);
        var byCustomer = netWorth.GetProperty("breakdowns").GetProperty("byCustomer").EnumerateArray().Single();
        byCustomer.GetProperty("currencies").EnumerateArray().Select(row => row.GetProperty("currency").GetString())
            .ShouldBe(["NZD", "USD"]);
        netWorth.GetProperty("breakdowns").EnumerateObject().Select(property => property.Name)
            .ShouldBe(["byCustomer", "byAccountType", "byCustodyMode", "topHoldings"]);
    }

    private static async Task FundedAsync(string accessToken, Guid customerId, string currency, decimal amount)
    {
        var accountId = await AccountHttp.OpenAccountAsync(accessToken, customerId, $"{currency} account", "Bank", currency);
        await PostAsync(accessToken, Cash(accountId, "TransferIn", amount, "2026-03-02T09:00:00Z"));
    }

    private static void AssertLine(JsonElement line, string currency, decimal total, string status, (decimal Rate, DateOnly Date)? fx, decimal? value)
    {
        line.GetProperty("currency").GetString().ShouldBe(currency);
        line.GetProperty("total").GetDecimal().ShouldBe(total);
        line.GetProperty("status").GetString().ShouldBe(status);
        if (fx is { } row)
        {
            line.GetProperty("fx").GetProperty("rate").GetDecimal().ShouldBe(row.Rate);
            line.GetProperty("fx").GetProperty("date").GetString().ShouldBe(row.Date.ToString("yyyy-MM-dd"));
        }
        else
        {
            line.GetProperty("fx").ValueKind.ShouldBe(JsonValueKind.Null);
        }

        if (value is { } amount)
        {
            line.GetProperty("value").GetDecimal().ShouldBe(amount);
        }
        else
        {
            line.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }
}

internal static class FoldedJson
{
    /// <summary>The <c>folded</c> object of a net-worth read; asserts that it is an object.</summary>
    public static JsonElement Folded(this JsonElement netWorth)
    {
        var folded = netWorth.GetProperty("folded");
        folded.ValueKind.ShouldBe(JsonValueKind.Object);
        return folded;
    }
}
