using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.AccountClosures;

/// <summary>Account closure R5, R8, R28: a closure with a payout creates its withdrawal, and the Account is closing.</summary>
[Parallelizable]
public class AccountClosurePayoutRequestTests : TestBase
{
    [Test]
    public async Task Request_WithAPayout_IsInProgress_AndCreatesTheWithdrawalOfThePayout()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 300.00m);
        await AccountClosureHttp.InsertCashAsync(world.AccountId, 0.004m);

        var id = await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);

        var item = await AccountClosureHttp.GetAsync(world.TenantAdmin, world.AccountId, id);
        item.GetProperty("status").GetString().ShouldBe("InProgress");
        item.GetProperty("cashBalanceAtRequest").GetDecimal().ShouldBe(300.004m);
        item.GetProperty("payoutAmount").GetDecimal().ShouldBe(300.00m);
        var payee = item.GetProperty("nominatedBankAccount");
        payee.GetProperty("id").GetGuid().ShouldBe(world.NominatedBankAccountId);
        payee.GetProperty("bankName").GetString().ShouldBe("ANZ");
        payee.GetProperty("accountName").GetString().ShouldBe("J Smith");
        payee.GetProperty("accountNumber").GetString().ShouldBe("****8900");
        foreach (var name in new[] { "cancelReason", "writeOffAmount", "writeOffTransactionId", "completedAt", "endedAt" })
        {
            item.GetProperty(name).ValueKind.ShouldBe(JsonValueKind.Null, name);
        }

        var withdrawalId = item.GetProperty("withdrawalId").GetGuid();
        var withdrawal = await WithdrawalHttp.GetAsync(world.TenantAdmin, withdrawalId);
        withdrawal.GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);
        withdrawal.GetProperty("amount").GetDecimal().ShouldBe(300.00m);
        withdrawal.GetProperty("status").GetString().ShouldBe("Requested");
        withdrawal.GetProperty("reference").GetString().ShouldBe(world.DepositReference);
        withdrawal.GetProperty("memo").GetString().ShouldBe(AccountClosureHttp.Memo);
        withdrawal.GetProperty("requestedBy").GetProperty("name").GetString().ShouldBe("Ann Adviser");
        withdrawal.GetProperty("requestedAt").GetDateTimeOffset().ShouldBe(item.GetProperty("requestedAt").GetDateTimeOffset());
        withdrawal.GetProperty("nominatedBankAccount").GetProperty("id").GetGuid().ShouldBe(world.NominatedBankAccountId);
        withdrawal.GetProperty("accountClosureId").GetGuid().ShouldBe(id);
        (await WithdrawalHttp.GetAsync(world.CustomerToken, withdrawalId)).GetProperty("accountClosureId").GetGuid().ShouldBe(id);

        var account = await AccountClosureHttp.AccountAsync(world.TenantAdmin, world.AccountId);
        account.GetProperty("status").GetString().ShouldBe("Open");
        account.GetProperty("cashBalance").GetDecimal().ShouldBe(300.004m);
        account.GetProperty("reservedCash").GetDecimal().ShouldBe(300.00m);
        account.GetProperty("availableCash").GetDecimal().ShouldBe(0.004m);
        var closure = account.GetProperty("closure");
        closure.GetProperty("id").GetGuid().ShouldBe(id);
        closure.GetProperty("status").GetString().ShouldBe("InProgress");
        closure.GetProperty("payoutAmount").GetDecimal().ShouldBe(300.00m);
        closure.GetProperty("withdrawalId").GetGuid().ShouldBe(withdrawalId);
        closure.GetProperty("completedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        (await AccountClosureHttp.ListIdsAsync(world.TenantAdmin, world.AccountId, "?status=InProgress")).ShouldBe([id]);
    }

    [Test]
    public async Task AnOrdinaryWithdrawal_HasNoAccountClosureId()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.RequestAsync(10m);

        (await WithdrawalHttp.GetAsync(world.TenantAdmin, id)).GetProperty("accountClosureId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Request_WithAPayout_ReplayedWithTheSameKey_ReturnsTheSameId_WithOneClosureAndOneWithdrawal()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 50m);
        var key = Guid.NewGuid();
        var body = AccountClosureHttp.Body(world.NominatedBankAccountId);

        var first = await CustodialHttp.ReadIdAsync(await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId, body, key));
        var again = await CustodialHttp.ReadIdAsync(await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId, body, key));

        again.ShouldBe(first);
        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldHaveSingleItem();
        (await AccountClosureHttp.WithdrawalsAsync(world.AccountId)).ShouldHaveSingleItem();
    }

    [Test]
    public async Task Request_WhileAClosureIsInProgress_Returns400AtAccountId()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 50m);
        await AccountClosureHttp.RequestAsync(world.Adviser, world.AccountId, world.NominatedBankAccountId);

        var errors = await CustodialHttp.ErrorsAsync(await AccountClosureHttp.PostAsync(
            world.TenantAdmin, world.AccountId, AccountClosureHttp.Body(world.NominatedBankAccountId)));

        errors["AccountId"].ShouldBe(["This account is already being closed."]);
        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldHaveSingleItem();
        (await AccountClosureHttp.WithdrawalsAsync(world.AccountId)).ShouldHaveSingleItem();
    }

    [Test]
    public async Task Request_WithAPayoutAndNoBankAccount_Returns400AtNominatedBankAccountId()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 300.00m);
        await AccountClosureHttp.InsertCashAsync(world.AccountId, 0.004m);

        var errors = await CustodialHttp.ErrorsAsync(await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId, AccountClosureHttp.Body()));

        errors["NominatedBankAccountId"].ShouldBe(["The account has 300.00 USD to pay out. Choose a verified bank account."]);
        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Request_ToAPendingOrAnotherCurrencysBankAccount_Returns400AtNominatedBankAccountId()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 50m);
        var pending = await NominatedBankAccountHttp.AddAsync(world.Adviser, world.Customer.PublicId, accountNumber: "222");
        var nzd = await NominatedBankAccountHttp.AddAsync(world.Adviser, world.Customer.PublicId, currency: "NZD", accountNumber: "444");
        await NominatedBankAccountHttp.VerifyAsync(world.TenantAdmin, nzd);

        (await CustodialHttp.ErrorsAsync(await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId, AccountClosureHttp.Body(pending))))
            ["NominatedBankAccountId"].ShouldBe(["The bank account is not verified."]);
        (await CustodialHttp.ErrorsAsync(await AccountClosureHttp.PostAsync(world.Adviser, world.AccountId, AccountClosureHttp.Body(nzd))))
            ["NominatedBankAccountId"].ShouldBe(["The bank account is in NZD; this account is USD."]);
        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldBeEmpty();
        (await AccountClosureHttp.WithdrawalsAsync(world.AccountId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Request_ToAnotherCustomersOrAnotherTenantsOrAMissingBankAccount_Returns404()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 50m);
        var south = await WithdrawalWorld.CreateAsync("South Advisory", "south-advisory", funding: 0m);
        var olgas = await NominatedBankAccountHttp.AddAsync(world.TenantAdmin, world.People.OtherCustomer.PublicId);
        await NominatedBankAccountHttp.VerifyAsync(world.SecondTenantAdmin, olgas);

        foreach (var nominated in new[] { olgas, south.NominatedBankAccountId, Guid.NewGuid() })
        {
            (await AccountClosureHttp.PostAsync(world.TenantAdmin, world.AccountId, AccountClosureHttp.Body(nominated)))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound, nominated.ToString());
        }

        (await AccountClosureHttp.ClosuresAsync(world.AccountId)).ShouldBeEmpty();
    }

    [Test]
    public async Task TheClosuresWithdrawal_IsApprovedByATenantAdminOtherThanTheRequester()
    {
        var world = await WithdrawalWorld.CreateAsync(funding: 50m);
        var id = await AccountClosureHttp.RequestAsync(world.TenantAdmin, world.AccountId, world.NominatedBankAccountId);
        var withdrawalId = (await AccountClosureHttp.GetAsync(world.TenantAdmin, world.AccountId, id)).GetProperty("withdrawalId").GetGuid();

        var own = await WithdrawalHttp.ActAsync(world.TenantAdmin, withdrawalId, "approve");
        var other = await WithdrawalHttp.ActAsync(world.SecondTenantAdmin, withdrawalId, "approve");

        own.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        other.StatusCode.ShouldBe(HttpStatusCode.NoContent, await other.Content.ReadAsStringAsync());
        (await AccountClosureHttp.WithdrawalsAsync(world.AccountId)).ShouldHaveSingleItem().Status.ShouldBe(WithdrawalStatus.Approved);
    }
}
