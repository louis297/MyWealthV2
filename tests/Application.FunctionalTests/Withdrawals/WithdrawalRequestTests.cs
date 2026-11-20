using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Application.FunctionalTests.Transactions;

namespace MyWealthV2.Application.FunctionalTests.Withdrawals;

/// <summary>Withdrawals R17–R26: request a withdrawal, reserve it, and read it.</summary>
[Parallelizable]
public class WithdrawalRequestTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401_OnTheReadAndRequestRoutes()
    {
        foreach (var (method, route, body) in WithdrawalHttp.Routes(Guid.NewGuid()).Take(3))
        {
            (await WithdrawalHttp.Send(method, route, accessToken: null, body))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route}");
        }
    }

    [Test]
    public async Task SystemAdmin_Returns403_AndACustomerCannotRequest()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var (method, route, body) in WithdrawalHttp.Routes(Guid.NewGuid()).Take(3))
        {
            (await WithdrawalHttp.Send(method, route, system.AccessToken, body))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {route}");
        }

        (await WithdrawalHttp.PostAsync(world.CustomerToken, WithdrawalHttp.Body(world.AccountId, world.NominatedBankAccountId, 1m)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Request_ByAnAdviser_Returns201_Requested_AndReservesTheAmount()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var body = WithdrawalHttp.Body(world.AccountId, world.NominatedBankAccountId, 300.00m);
        body["memo"] = "  Customer asked by phone ";

        var response = await WithdrawalHttp.PostAsync(world.Adviser, body);

        var id = await CustodialHttp.ReadIdAsync(response);
        response.Headers.Location!.ToString().ShouldBe($"/withdrawals/{id}");
        var item = await WithdrawalHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);
        item.GetProperty("customerId").GetGuid().ShouldBe(world.Customer.PublicId);
        var payee = item.GetProperty("nominatedBankAccount");
        payee.GetProperty("id").GetGuid().ShouldBe(world.NominatedBankAccountId);
        payee.GetProperty("bankName").GetString().ShouldBe("ANZ");
        payee.GetProperty("accountName").GetString().ShouldBe("J Smith");
        payee.GetProperty("accountNumber").GetString().ShouldBe("****8900");
        item.GetProperty("amount").GetDecimal().ShouldBe(300.00m);
        item.GetProperty("currency").GetString().ShouldBe("USD");
        item.GetProperty("reference").GetString().ShouldBe(world.DepositReference);
        item.GetProperty("memo").GetString().ShouldBe("Customer asked by phone");
        item.GetProperty("status").GetString().ShouldBe("Requested");
        item.GetProperty("requestedBy").GetProperty("name").GetString().ShouldBe("Ann Adviser");
        item.GetProperty("requestedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        foreach (var name in new[] { "decision", "payment", "payoutTransactionId", "returnTransactionId", "failureReason", "returnReason" })
        {
            item.GetProperty(name).ValueKind.ShouldBe(JsonValueKind.Null, name);
        }

        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        (await world.CashAsync()).ShouldBe((1000.00m, 300.00m, 700.00m));
    }

    [Test]
    public async Task Request_WithAReference_KeepsIt()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var body = WithdrawalHttp.Body(world.AccountId, world.NominatedBankAccountId, 10m);
        body["reference"] = " Rent Nov-2026. ";

        var id = await CustodialHttp.ReadIdAsync(await WithdrawalHttp.PostAsync(world.TenantAdmin, body));

        (await WithdrawalHttp.GetAsync(world.TenantAdmin, id)).GetProperty("reference").GetString().ShouldBe("Rent Nov-2026.");
    }

    [Test]
    public async Task Request_ReplayedWithTheSameKey_ReturnsTheSameId_AndAnotherBodyIs409_AndNoKeyIs400()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var key = Guid.NewGuid();
        var body = WithdrawalHttp.Body(world.AccountId, world.NominatedBankAccountId, 100m);

        var first = await CustodialHttp.ReadIdAsync(await WithdrawalHttp.PostAsync(world.Adviser, body, key));
        var again = await CustodialHttp.ReadIdAsync(await WithdrawalHttp.PostAsync(world.Adviser, body, key));
        var other = await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(world.AccountId, world.NominatedBankAccountId, 101m), key);
        var noKey = await TransactionHttp.Send(HttpMethod.Post, WithdrawalHttp.Path, world.Adviser, body);

        again.ShouldBe(first);
        other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        noKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await WithdrawalHttp.ListIdsAsync(world.TenantAdmin)).ShouldBe([first]);
        (await world.CashAsync()).Reserved.ShouldBe(100m);
    }

    [Test]
    public async Task Request_FromAnExternalAccount_Returns400AtAccountId()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin, world.Customer.PublicId);
        (await CustodialHttp.FundAsync(world.TenantAdmin, external, 50m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(external, world.NominatedBankAccountId, 10m));

        (await CustodialHttp.ErrorsAsync(response))["AccountId"]
            .ShouldBe(["A withdrawal can be requested only from a Custodial account."]);
    }

    [Test]
    public async Task Request_FromAClosedAccount_Returns400()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        (await CustodialHttp.CloseAsync(world.TenantAdmin, world.AccountId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(world.AccountId, world.NominatedBankAccountId, 10m));

        (await CustodialHttp.ErrorsAsync(response)).Values.SelectMany(messages => messages)
            .ShouldContain("Cannot post a transaction while the account is closed. Reopen the account first.");
    }

    [Test]
    public async Task Request_ForADisabledCustomer_Returns400DisabledUser()
    {
        var world = await WithdrawalWorld.CreateAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);

        var response = await WithdrawalHttp.PostAsync(world.TenantAdmin, WithdrawalHttp.Body(world.AccountId, world.NominatedBankAccountId, 10m));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
    }

    [Test]
    public async Task Request_ToAnotherCustomersOrAnotherTenantsOrAMissingNominatedAccount_Returns404()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var south = await WithdrawalWorld.CreateAsync("South Advisory", "south-advisory");
        var olgas = await NominatedBankAccountHttp.AddAsync(world.TenantAdmin, world.People.OtherCustomer.PublicId);
        await NominatedBankAccountHttp.VerifyAsync(world.SecondTenantAdmin, olgas);

        foreach (var nominated in new[] { olgas, south.NominatedBankAccountId, Guid.NewGuid() })
        {
            (await WithdrawalHttp.PostAsync(world.TenantAdmin, WithdrawalHttp.Body(world.AccountId, nominated, 10m)))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound, nominated.ToString());
        }
    }

    [Test]
    public async Task Request_FromAnUnassignedAnotherTenantsOrAMissingAccount_Returns404()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var south = await WithdrawalWorld.CreateAsync("South Advisory", "south-advisory");
        var olgas = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin, world.People.OtherCustomer.PublicId);

        foreach (var account in new[] { olgas, south.AccountId, Guid.NewGuid() })
        {
            (await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(account, world.NominatedBankAccountId, 10m)))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound, account.ToString());
        }
    }

    [Test]
    public async Task Request_ToAPendingOrRemovedNominatedAccount_Returns400_AndToAnotherCurrency_Returns400()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var pending = await NominatedBankAccountHttp.AddAsync(world.Adviser, world.Customer.PublicId, accountNumber: "222");
        var removed = await NominatedBankAccountHttp.AddAsync(world.Adviser, world.Customer.PublicId, accountNumber: "333");
        await NominatedBankAccountHttp.VerifyAsync(world.TenantAdmin, removed);
        (await NominatedBankAccountHttp.ActAsync(world.Adviser, removed, "remove", "Closed")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var nzd = await NominatedBankAccountHttp.AddAsync(world.Adviser, world.Customer.PublicId, currency: "NZD", accountNumber: "444");
        await NominatedBankAccountHttp.VerifyAsync(world.TenantAdmin, nzd);

        foreach (var nominated in new[] { pending, removed })
        {
            (await CustodialHttp.ErrorsAsync(await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(world.AccountId, nominated, 10m))))
                ["NominatedBankAccountId"].ShouldBe(["The bank account is not verified."]);
        }

        (await CustodialHttp.ErrorsAsync(await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(world.AccountId, nzd, 10m))))
            ["NominatedBankAccountId"].ShouldBe(["The bank account is in NZD; this account is USD."]);
    }

    [TestCase(1000.01, "The account's available cash is 1000.00 USD.")]
    [TestCase(10.001, null)]
    [TestCase(0, null)]
    [TestCase(-5, null)]
    public async Task Request_WithABadAmount_Returns400AtAmount(decimal amount, string? message)
    {
        var world = await WithdrawalWorld.CreateAsync();

        var errors = await CustodialHttp.ErrorsAsync(
            await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(world.AccountId, world.NominatedBankAccountId, amount)));

        errors.ShouldContainKey("Amount");
        if (message is not null)
        {
            errors["Amount"].ShouldBe([message]);
        }

        (await world.CashAsync()).Reserved.ShouldBe(0m);
    }

    [Test]
    public async Task TwoRequests_ThatTogetherExceedTheCash_RefuseTheSecond()
    {
        var world = await WithdrawalWorld.CreateAsync();
        await world.RequestAsync(700m);

        var errors = await CustodialHttp.ErrorsAsync(
            await WithdrawalHttp.PostAsync(world.Adviser, WithdrawalHttp.Body(world.AccountId, world.NominatedBankAccountId, 300.01m)));

        errors["Amount"].ShouldBe(["The account's available cash is 300.00 USD."]);
        await world.RequestAsync(300m);
        (await world.CashAsync()).ShouldBe((1000m, 1000m, 0m));
    }

    [TestCase("reference", "")]
    [TestCase("reference", "ABCDEFGHIJKLMNOPQRS")]
    [TestCase("reference", "Rent/Nov")]
    [TestCase("memo", "201")]
    [TestCase("memo", " ")]
    [TestCase("tenantId", "a1b2c3d4-0000-0000-0000-000000000001")]
    public async Task Request_WithABadReferenceMemoOrATenantId_Returns400(string field, string value)
    {
        var world = await WithdrawalWorld.CreateAsync();
        var body = WithdrawalHttp.Body(world.AccountId, world.NominatedBankAccountId, 10m);
        body[field] = value == "201" ? new string('m', 201) : value;

        var response = await WithdrawalHttp.PostAsync(world.Adviser, body);

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey(char.ToUpperInvariant(field[0]) + field[1..]);
    }

    [Test]
    public async Task ACustomer_ReadsTheirOwnWithdrawals_WithoutTheMemo()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var body = WithdrawalHttp.Body(world.AccountId, world.NominatedBankAccountId, 10m);
        body["memo"] = "Internal";
        var id = await CustodialHttp.ReadIdAsync(await WithdrawalHttp.PostAsync(world.Adviser, body));
        var olgas = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin, world.People.OtherCustomer.PublicId);
        (await CustodialHttp.FundAsync(world.TenantAdmin, olgas, 50m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var olgasPayee = await NominatedBankAccountHttp.AddAsync(world.TenantAdmin, world.People.OtherCustomer.PublicId);
        await NominatedBankAccountHttp.VerifyAsync(world.SecondTenantAdmin, olgasPayee);
        var other = await WithdrawalHttp.RequestAsync(world.TenantAdmin, olgas, olgasPayee, 5m);

        var item = await WithdrawalHttp.GetAsync(world.CustomerToken, id);
        item.GetProperty("memo").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("amount").GetDecimal().ShouldBe(10m);
        (await WithdrawalHttp.ListIdsAsync(world.CustomerToken)).ShouldBe([id]);
        (await WithdrawalHttp.Send(HttpMethod.Get, $"{WithdrawalHttp.Path}/{other}", world.CustomerToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task List_IsNewestFirst_AndFiltersByAccountCustomerAndStatus_AndAnAdviserSeesOnlyAssigned()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var second = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin, world.Customer.PublicId, "Second");
        (await CustodialHttp.FundAsync(world.TenantAdmin, second, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var a = await world.RequestAsync(10m);
        var b = await WithdrawalHttp.RequestAsync(world.Adviser, second, world.NominatedBankAccountId, 20m);
        var olgas = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin, world.People.OtherCustomer.PublicId);
        (await CustodialHttp.FundAsync(world.TenantAdmin, olgas, 50m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var olgasPayee = await NominatedBankAccountHttp.AddAsync(world.TenantAdmin, world.People.OtherCustomer.PublicId);
        await NominatedBankAccountHttp.VerifyAsync(world.SecondTenantAdmin, olgasPayee);
        var c = await WithdrawalHttp.RequestAsync(world.TenantAdmin, olgas, olgasPayee, 5m);

        (await WithdrawalHttp.ListIdsAsync(world.TenantAdmin)).ShouldBe([c, b, a]);
        (await WithdrawalHttp.ListIdsAsync(world.TenantAdmin, $"?accountId={second}")).ShouldBe([b]);
        (await WithdrawalHttp.ListIdsAsync(world.TenantAdmin, $"?customerId={world.Customer.PublicId}")).ShouldBe([b, a]);
        (await WithdrawalHttp.ListIdsAsync(world.TenantAdmin, "?status=requested")).ShouldBe([c, b, a]);
        (await WithdrawalHttp.ListIdsAsync(world.TenantAdmin, "?status=Paid,Rejected")).ShouldBeEmpty();
        (await WithdrawalHttp.ListIdsAsync(world.Adviser)).ShouldBe([b, a]);
        (await WithdrawalHttp.Send(HttpMethod.Get, $"{WithdrawalHttp.Path}/{c}", world.Adviser)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CustodialHttp.ErrorsAsync(await WithdrawalHttp.Send(HttpMethod.Get, $"{WithdrawalHttp.Path}?status=Gone", world.TenantAdmin)))
            .ShouldContainKey("Status");
        (await WithdrawalHttp.Send(HttpMethod.Get, $"{WithdrawalHttp.Path}?tenantId={world.Tenant.PublicId}", world.TenantAdmin))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AnotherTenantsWithdrawal_Returns404_AndIsNotListed()
    {
        var north = await WithdrawalWorld.CreateAsync();
        var south = await WithdrawalWorld.CreateAsync("South Advisory", "south-advisory");
        var id = await south.RequestAsync(10m);

        (await WithdrawalHttp.Send(HttpMethod.Get, $"{WithdrawalHttp.Path}/{id}", north.TenantAdmin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await WithdrawalHttp.ListIdsAsync(north.TenantAdmin)).ShouldBeEmpty();
    }
}
