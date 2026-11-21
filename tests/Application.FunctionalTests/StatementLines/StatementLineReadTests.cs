using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.StatementLines;

/// <summary>Deposit matching R21, R22: reading suspense.</summary>
[Parallelizable]
public class StatementLineReadTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect_OnEveryRoute()
    {
        foreach (var (method, route, body) in StatementLineHttp.Routes(Guid.NewGuid()))
        {
            var response = await StatementLineHttp.Send(method, route, accessToken: null, body);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route}");
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Test]
    public async Task AdviserCustomerAndSystemAdmin_Return403_OnEveryRoute_EvenForTheirOwnLine()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(10m, null);
        var adviser = await world.SignInAdviserAsync();
        var customer = await TestApp.IssueAccessTokenAsync(world.Customer);
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var token in new[] { adviser.AccessToken, customer.AccessToken, system.AccessToken })
        {
            foreach (var (method, route, body) in StatementLineHttp.Routes(line.PublicId))
            {
                (await StatementLineHttp.Send(method, route, token, body))
                    .StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {route}");
            }
        }
    }

    [Test]
    public async Task List_IsNewestFirst_AndOnlyTheTenantsLines()
    {
        var south = await DepositMatchingWorld.CreateAsync("South Advisory", "south-advisory");
        await south.ReceiveAsync(1m, null);
        var world = await DepositMatchingWorld.CreateAsync();
        var first = await world.ReceiveAsync(1m, null);
        var second = await world.ReceiveAsync(2m, world.DepositReference);
        var third = await world.ReceiveAsync(3m, "Rent");

        var page = await StatementLineHttp.ListAsync(world.TenantAdmin.AccessToken);

        page.GetProperty("totalCount").GetInt32().ShouldBe(3);
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe([third.PublicId, second.PublicId, first.PublicId]);
        (await StatementLineHttp.ListIdsAsync(world.TenantAdmin.AccessToken, "?page=2&pageSize=2"))
            .ShouldBe([first.PublicId]);
    }

    [Test]
    public async Task Item_OfAMatchedAndOfAnUnmatchedLine()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var matched = await world.ReceiveAsync(500.00m, world.DepositReference);
        var unmatched = await world.ReceiveAsync(7m, "Rent for November");
        var deposit = (await BankFeedHttp.TransactionsOfLineAsync(matched.PublicId)).Single();

        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, matched.PublicId);

        item.GetProperty("id").GetGuid().ShouldBe(matched.PublicId);
        item.GetProperty("clientMoneyAccountId").GetGuid().ShouldBe(world.ClientMoney.PublicId);
        item.GetProperty("currency").GetString().ShouldBe("USD");
        item.GetProperty("direction").GetString().ShouldBe("Credit");
        item.GetProperty("amount").GetDecimal().ShouldBe(500.00m);
        item.GetProperty("valueDate").GetString().ShouldBe(matched.ValueDate.ToString("yyyy-MM-dd"));
        item.GetProperty("payerName").GetString().ShouldBe("J SMITH");
        item.GetProperty("payerAccount").GetString().ShouldBe("****4200");
        item.GetProperty("reference").GetString().ShouldBe(world.DepositReference);
        item.GetProperty("receivedAt").GetDateTimeOffset().ShouldBe(matched.ReceivedAt, TimeSpan.FromMilliseconds(1));
        item.GetProperty("status").GetString().ShouldBe("Matched");
        item.GetProperty("unmatchedReason").ValueKind.ShouldBe(JsonValueKind.Null);
        var match = item.GetProperty("match");
        match.GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);
        match.GetProperty("customerId").GetGuid().ShouldBe(world.Customer.PublicId);
        match.GetProperty("transactionId").GetGuid().ShouldBe(deposit.PublicId);
        match.GetProperty("method").GetString().ShouldBe("Reference");
        match.GetProperty("at").GetDateTimeOffset().ShouldNotBe(default);
        item.GetProperty("lastAction").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();

        var other = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, unmatched.PublicId);
        other.GetProperty("status").GetString().ShouldBe("Unmatched");
        other.GetProperty("unmatchedReason").GetString().ShouldBe("NoReference");
        other.GetProperty("match").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task List_FiltersByStatusCurrencyAndClientMoneyAccount()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var matched = await world.ReceiveAsync(1m, world.DepositReference);
        var unmatched = await world.ReceiveAsync(2m, null);
        var nzdClientMoney = await BankFeedHttp.ClientMoneyAccountAsync(world.Tenant.Id, "NZD");
        var nzdSent = BankFeedHttp.Feed.AddReceipt(nzdClientMoney.AccountNumber, 3m, "NZD", null);
        var debitSent = BankFeedHttp.Feed.AddReceipt(
            world.ClientMoney.AccountNumber, 4m, "USD", null, direction: BankLineDirection.Debit);
        await BankFeedHttp.RunAsync();
        var nzd = await BankFeedHttp.LineAsync(nzdSent.Id!);
        var debit = await BankFeedHttp.LineAsync(debitSent.Id!);
        var token = world.TenantAdmin.AccessToken;

        (await StatementLineHttp.ListIdsAsync(token, "?status=Matched")).ShouldBe([matched.PublicId]);
        (await StatementLineHttp.ListIdsAsync(token, "?status=Unmatched&direction=Credit"))
            .ShouldBe([nzd.PublicId, unmatched.PublicId]);
        (await StatementLineHttp.ListIdsAsync(token, "?status=Matched,%20Unmatched&direction=Credit"))
            .ShouldBe([nzd.PublicId, unmatched.PublicId, matched.PublicId]);
        (await StatementLineHttp.ListIdsAsync(token, "?status=Unmatched&direction=Debit")).ShouldBe([debit.PublicId]);
        (await StatementLineHttp.ListIdsAsync(token, "?status=Received")).ShouldBeEmpty();
        (await StatementLineHttp.ListIdsAsync(token, "?currency=nzd")).ShouldBe([nzd.PublicId]);
        (await StatementLineHttp.ListIdsAsync(token, $"?clientMoneyAccountId={world.ClientMoney.PublicId}&status=Unmatched&direction=Credit"))
            .ShouldBe([unmatched.PublicId]);
        (await StatementLineHttp.ListIdsAsync(token, "?status=Ignored,ReturnRequested")).ShouldBeEmpty();
    }

    [TestCase("?status=Lost")]
    [TestCase("?status=Matched,Lost")]
    [TestCase("?status=1")]
    public async Task List_UnknownStatus_Returns400AtStatus(string query)
    {
        var world = await DepositMatchingWorld.CreateAsync();

        var response = await StatementLineHttp.Send(HttpMethod.Get, StatementLineHttp.Path + query, world.TenantAdmin.AccessToken);

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey("Status");
    }

    [Test]
    public async Task List_WithATenantId_Returns400()
    {
        var world = await DepositMatchingWorld.CreateAsync();

        var response = await StatementLineHttp.Send(
            HttpMethod.Get, $"{StatementLineHttp.Path}?tenantId={world.Tenant.PublicId}", world.TenantAdmin.AccessToken);

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey("TenantId");
    }

    [Test]
    public async Task TwoTenants_AnotherTenantsLineOrAMissingOne_Returns404()
    {
        var south = await DepositMatchingWorld.CreateAsync("South Advisory", "south-advisory");
        var southLine = await south.ReceiveAsync(1m, null);
        var world = await DepositMatchingWorld.CreateAsync();

        (await StatementLineHttp.Send(HttpMethod.Get, $"{StatementLineHttp.Path}/{southLine.PublicId}", world.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StatementLineHttp.Send(HttpMethod.Get, $"{StatementLineHttp.Path}/{Guid.NewGuid()}", world.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StatementLineHttp.ListIdsAsync(world.TenantAdmin.AccessToken, $"?clientMoneyAccountId={south.ClientMoney.PublicId}"))
            .ShouldBeEmpty();
    }

    [Test]
    public async Task DisabledTenant_Returns403()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await TenantDisabler.DisableAsync(world.Tenant.Id);

        (await StatementLineHttp.Send(HttpMethod.Get, StatementLineHttp.Path, world.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
