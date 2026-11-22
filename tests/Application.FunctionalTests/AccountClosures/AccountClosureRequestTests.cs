using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.AccountClosures;

/// <summary>Account closure R1–R7, R9, R10, R18, R24–R27: request an account closure with nothing to pay out, and
/// read closures.</summary>
[Parallelizable]
public class AccountClosureRequestTests : TestBase
{
    private const string ClosedMessage = "Cannot post a transaction while the account is closed. Reopen the account first.";

    [Test]
    public async Task WithoutBearer_Returns401_OnAllThreeRoutes()
    {
        foreach (var (method, route, body) in AccountClosureHttp.Routes(Guid.NewGuid(), Guid.NewGuid()))
        {
            (await AccountClosureHttp.SendRoute(method, route, accessToken: null, body))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route}");
        }
    }

    [Test]
    public async Task SystemAdmin_Returns403_OnAllThreeRoutes_AndACustomerCannotRequest()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var (method, route, body) in AccountClosureHttp.Routes(world.AccountId, Guid.NewGuid()))
        {
            (await AccountClosureHttp.SendRoute(method, route, system.AccessToken, body))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {route}");
        }

        (await AccountClosureHttp.PostAsync(world.CustomerToken, world.AccountId, AccountClosureHttp.Body()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Request_WithOnlyARemainder_WritesItOff_ClosesTheAccount_AndIsCompleted()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        await AccountClosureHttp.InsertCashAsync(world.AccountId, 0.004m);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId, AccountClosureHttp.Body(memo: "  Moving away "));

        var id = await CustodialHttp.ReadIdAsync(response);
        response.Headers.Location!.ToString().ShouldBe($"/accounts/{world.AccountId}/closures/{id}");
        var item = await AccountClosureHttp.GetAsync(world.TenantAdmin, world.AccountId, id);
        item.GetProperty("id").GetGuid().ShouldBe(id);
        item.GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);
        item.GetProperty("customerId").GetGuid().ShouldBe(world.Customer.PublicId);
        item.GetProperty("status").GetString().ShouldBe("Completed");
        item.GetProperty("memo").GetString().ShouldBe("Moving away");
        item.GetProperty("requestedBy").GetProperty("id").GetGuid().ShouldBe(world.People.Adviser.PublicId);
        item.GetProperty("requestedBy").GetProperty("name").GetString().ShouldBe("Ann Adviser");
        item.GetProperty("requestedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        item.GetProperty("currency").GetString().ShouldBe("USD");
        item.GetProperty("cashBalanceAtRequest").GetDecimal().ShouldBe(0.004m);
        item.GetProperty("payoutAmount").GetDecimal().ShouldBe(0m);
        item.GetProperty("writeOffAmount").GetDecimal().ShouldBe(0.004m);
        item.GetProperty("completedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        foreach (var name in new[] { "cancelReason", "nominatedBankAccount", "withdrawalId", "endedAt" })
        {
            item.GetProperty(name).ValueKind.ShouldBe(JsonValueKind.Null, name);
        }

        item.TryGetProperty("rowVersion", out _).ShouldBeFalse();

        var writeOffId = item.GetProperty("writeOffTransactionId").GetGuid();
        using (var writeOff = await CustodialHttp.GetTransactionAsync(world.TenantAdmin, writeOffId))
        {
            var root = writeOff.RootElement;
            root.GetProperty("type").GetString().ShouldBe("CloseOut");
            root.GetProperty("memo").GetString().ShouldBe("Account closure remainder");
            root.GetProperty("reference").ValueKind.ShouldBe(JsonValueKind.Null);
            root.GetProperty("source").GetProperty("kind").GetString().ShouldBe("AccountClosure");
            root.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(id);
            var leg = root.GetProperty("cash").EnumerateArray().ShouldHaveSingleItem();
            leg.GetProperty("amount").GetDecimal().ShouldBe(-0.004m);
        }

        (await AccountClosureHttp.TransactionAsync(writeOffId)).CreatedBy.ShouldBe(world.People.Adviser.IdentityUserId);
        var account = await AccountClosureHttp.AccountAsync(world.TenantAdmin, world.AccountId);
        account.GetProperty("status").GetString().ShouldBe("Closed");
        account.GetProperty("cashBalance").GetDecimal().ShouldBe(0m);
        var closure = account.GetProperty("closure");
        closure.GetProperty("id").GetGuid().ShouldBe(id);
        closure.GetProperty("status").GetString().ShouldBe("Completed");
        closure.GetProperty("payoutAmount").GetDecimal().ShouldBe(0m);
        closure.GetProperty("withdrawalId").ValueKind.ShouldBe(JsonValueKind.Null);
        closure.GetProperty("requestedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        closure.GetProperty("completedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        (await AccountClosureHttp.WithdrawalsAsync(world.AccountId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Request_WithNothing_ClosesTheAccount_WithNoWriteOff_AndDoesNotCheckABankAccount()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);

        var id = await AccountClosureHttp.RequestAsync(world.TenantAdmin, world.AccountId, nominatedBankAccountId: Guid.NewGuid());

        var item = await AccountClosureHttp.GetAsync(world.TenantAdmin, world.AccountId, id);
        item.GetProperty("status").GetString().ShouldBe("Completed");
        item.GetProperty("cashBalanceAtRequest").GetDecimal().ShouldBe(0m);
        item.GetProperty("nominatedBankAccount").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("writeOffAmount").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("writeOffTransactionId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await AccountClosureHttp.AccountAsync(world.TenantAdmin, world.AccountId)).GetProperty("status").GetString().ShouldBe("Closed");
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(0);
        var stored = (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldHaveSingleItem();
        stored.NominatedBankAccountId.ShouldBeNull();
    }

    [Test]
    public async Task Request_ReplayedWithTheSameKey_ReturnsTheSameId_AndAnotherBodyIs409_AndNoKeyIs400()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        var key = Guid.NewGuid();

        var first = await CustodialHttp.ReadIdAsync(await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId, AccountClosureHttp.Body(), key));
        var again = await CustodialHttp.ReadIdAsync(await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId, AccountClosureHttp.Body(), key));
        var other = await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId, AccountClosureHttp.Body(memo: "Other"), key);
        var noKey = await TransactionHttp.Send(HttpMethod.Post, AccountClosureHttp.Path(world.AccountId), world.Adviser, AccountClosureHttp.Body());

        again.ShouldBe(first);
        other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        noKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldHaveSingleItem();
    }

    [Test]
    public async Task Request_OnAnExternalAccount_Returns400AtAccountId()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin, world.Customer.PublicId);

        var errors = await CustodialHttp.ErrorsAsync(await AccountClosureHttp.PostAsync(world.Adviser, external, AccountClosureHttp.Body()));

        errors["AccountId"].ShouldBe(["Only a Custodial account is closed with an account closure."]);
        (await AccountClosureHttp.ClosuresAsync(external)).ShouldBeEmpty();
    }

    [Test]
    public async Task Request_OnAClosedAccount_Returns400()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId);

        var errors = await CustodialHttp.ErrorsAsync(await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId, AccountClosureHttp.Body()));

        errors[string.Empty].ShouldBe([ClosedMessage]);
        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldHaveSingleItem();
    }

    [Test]
    public async Task Request_ForADisabledCustomer_Returns400DisabledUser()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);

        var response = await AccountClosureHttp.PostAsync(world.TenantAdmin, world.AccountId, AccountClosureHttp.Body());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Request_WithAHolding_Returns400AtAccountId()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 100m);
        var instrument = await CustodialHttp.CreateInstrumentAsync(world.TenantAdmin, "VTI", "USD", 25m);
        (await CustodialHttp.PostAsync(world.Adviser, CustodialHttp.Trade(world.AccountId, "Buy", instrument, 1m)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var errors = await CustodialHttp.ErrorsAsync(await AccountClosureHttp.PostAsync(
            world.Adviser, world.AccountId, AccountClosureHttp.Body(world.NominatedBankAccountId)));

        errors["AccountId"].ShouldBe(["Sell every holding before closing the account."]);
        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Request_WithAnActiveAssignment_Returns400AtAccountId()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        await AccountClosureHttp.InsertActiveAssignmentAsync(world.TenantAdmin, world.AccountId);

        var errors = await CustodialHttp.ErrorsAsync(await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId, AccountClosureHttp.Body()));

        errors["AccountId"].ShouldBe(["Unassign the portfolio before closing the account."]);
    }

    [Test]
    public async Task Request_WithAnOpenWithdrawal_Returns400AtAccountId()
    {
        var world = await WithdrawalWorld.CreateAsync();
        await world.RequestAsync(100m);

        var errors = await CustodialHttp.ErrorsAsync(await AccountClosureHttp.PostAsync(
            world.Adviser, world.AccountId, AccountClosureHttp.Body(world.NominatedBankAccountId)));

        errors["AccountId"].ShouldBe(["Open withdrawals must be paid, rejected or cancelled before the account can be closed."]);
        (await AccountClosureHttp.WithdrawalsAsync(world.AccountId)).ShouldHaveSingleItem();
    }

    [TestCase(null)]
    [TestCase("   ")]
    [TestCase("201")]
    public async Task Request_WithoutAMemoOrWithALongOne_Returns400AtMemo(string? memo)
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);

        var errors = await CustodialHttp.ErrorsAsync(await AccountClosureHttp.PostAsync(
            world.Adviser, world.AccountId, AccountClosureHttp.Body(memo: memo == "201" ? new string('m', 201) : memo)));

        errors.ShouldContainKey("Memo");
        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldBeEmpty();
    }

    [TestCase("accountId")]
    [TestCase("status")]
    [TestCase("amount")]
    [TestCase("id")]
    [TestCase("tenantId")]
    public async Task Request_WithAFieldTheBodyMustNotCarry_Returns400(string field)
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        var body = AccountClosureHttp.Body();
        body[field] = field switch
        {
            "status" => "Completed",
            "amount" => 10m,
            _ => (object)world.AccountId
        };

        var response = await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Request_OnAnUnassignedAnotherTenantsOrAMissingAccount_Returns404()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        var south = await WithdrawalWorld.CreateAsync("South Advisory", "south-advisory", funding: 0m);
        var olgas = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin, world.People.OtherCustomer.PublicId);

        foreach (var account in new[] { olgas, south.AccountId, Guid.NewGuid() })
        {
            (await AccountClosureHttp.PostAsync(world.Adviser, account, AccountClosureHttp.Body()))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound, account.ToString());
        }

        (await AccountClosureHttp.ClosuresAsync(olgas)).ShouldBeEmpty();
        (await AccountClosureHttp.ClosuresAsync(south.AccountId)).ShouldBeEmpty();
    }

    [Test]
    public async Task AnotherTenantsAccount_Returns404_OnAllThreeRoutes()
    {
        var north = await WithdrawalWorld.CreateAsync(funding: 0m);
        var south = await WithdrawalWorld.CreateAsync("South Advisory", "south-advisory", funding: 0m);
        var closure = await AccountClosureHttp.RequestAsync(south.Adviser, south.AccountId);

        foreach (var (method, route, body) in AccountClosureHttp.Routes(south.AccountId, closure))
        {
            (await AccountClosureHttp.SendRoute(method, route, north.TenantAdmin, body))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{method} {route}");
        }

        (await AccountClosureHttp.ClosuresAsync(south.AccountId)).ShouldHaveSingleItem();
    }

    [Test]
    public async Task AClosureOfAnotherAccount_UnderThisAccountsPath_Returns404()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        var second = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin, world.Customer.PublicId, "Second");
        var closureOfSecond = await AccountClosureHttp.RequestAsync(world.Adviser, second);

        (await AccountClosureHttp.Send(HttpMethod.Get, $"{AccountClosureHttp.Path(world.AccountId)}/{closureOfSecond}", world.TenantAdmin))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AccountClosureHttp.Send(HttpMethod.Get, $"{AccountClosureHttp.Path(world.AccountId)}/{Guid.NewGuid()}", world.TenantAdmin))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AccountClosureHttp.ListIdsAsync(world.TenantAdmin, world.AccountId)).ShouldBeEmpty();
    }

    [Test]
    public async Task ACustomer_ReadsTheirOwnClosures_WithoutTheMemo_AndNotAnotherCustomers()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId);
        var olgas = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin, world.People.OtherCustomer.PublicId);
        var other = await AccountClosureHttp.RequestAsync(world.TenantAdmin, olgas);

        var item = await AccountClosureHttp.GetAsync(world.CustomerToken, world.AccountId, id);
        item.GetProperty("memo").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("status").GetString().ShouldBe("Completed");
        (await AccountClosureHttp.ListIdsAsync(world.CustomerToken, world.AccountId)).ShouldBe([id]);
        (await AccountClosureHttp.AccountAsync(world.CustomerToken, world.AccountId))
            .GetProperty("closure").GetProperty("id").GetGuid().ShouldBe(id);
        (await AccountClosureHttp.Send(HttpMethod.Get, AccountClosureHttp.Path(olgas), world.CustomerToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AccountClosureHttp.Send(HttpMethod.Get, $"{AccountClosureHttp.Path(olgas)}/{other}", world.CustomerToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task List_IsNewestFirst_FiltersByStatus_AndRefusesAnUnknownStatusOrATenantId()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        var first = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId);
        await AccountClosureHttp.ReopenAsync(world.TenantAdmin, world.AccountId);
        var second = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId);

        (await AccountClosureHttp.ListIdsAsync(world.TenantAdmin, world.AccountId)).ShouldBe([second, first]);
        (await AccountClosureHttp.ListIdsAsync(world.TenantAdmin, world.AccountId, "?status=completed")).ShouldBe([second, first]);
        (await AccountClosureHttp.ListIdsAsync(world.TenantAdmin, world.AccountId, "?status=InProgress,Cancelled")).ShouldBeEmpty();
        (await AccountClosureHttp.AccountAsync(world.TenantAdmin, world.AccountId))
            .GetProperty("closure").GetProperty("id").GetGuid().ShouldBe(second);
        (await CustodialHttp.ErrorsAsync(await AccountClosureHttp.Send(
                HttpMethod.Get, AccountClosureHttp.Path(world.AccountId) + "?status=Gone", world.TenantAdmin)))
            .ShouldContainKey("Status");
        (await AccountClosureHttp.Send(
                HttpMethod.Get, AccountClosureHttp.Path(world.AccountId) + $"?tenantId={world.Tenant.PublicId}", world.TenantAdmin))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task TheAccountList_CarriesTheLatestClosure_AndNullOnAnExternalOrUnclosedAccount()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin, world.Customer.PublicId);
        var open = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin, world.Customer.PublicId, "Still open");
        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId);

        var response = await AccountClosureHttp.Send(
            HttpMethod.Get, $"/accounts?customerId={world.Customer.PublicId}&pageSize=50", world.TenantAdmin);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetGuid(), item => item.GetProperty("closure").Clone());

        items[world.AccountId].GetProperty("id").GetGuid().ShouldBe(id);
        items[world.AccountId].GetProperty("status").GetString().ShouldBe("Completed");
        items[external].ValueKind.ShouldBe(JsonValueKind.Null);
        items[open].ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task TheWriteOff_CannotBeReversedByHand_AfterTheAccountIsReopened()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 0m);
        await AccountClosureHttp.InsertCashAsync(world.AccountId, 0.004m);
        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId);
        var writeOff = (await AccountClosureHttp.GetAsync(world.TenantAdmin, world.AccountId, id))
            .GetProperty("writeOffTransactionId").GetGuid();
        await AccountClosureHttp.ReopenAsync(world.TenantAdmin, world.AccountId);

        var errors = await CustodialHttp.ErrorsAsync(await CustodialHttp.ReverseAsync(world.TenantAdmin, writeOff));

        errors[string.Empty].ShouldBe(["This transaction was written by the platform and cannot be reversed by hand."]);
        (await AccountClosureHttp.TransactionAsync(writeOff)).ReversedByTransactionId.ShouldBeNull();
    }
}
