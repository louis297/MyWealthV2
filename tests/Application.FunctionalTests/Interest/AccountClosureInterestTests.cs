using System.Net;
using MyWealthV2.Application.FunctionalTests.AccountClosures;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.Interest.InterestDb;
using static MyWealthV2.Application.FunctionalTests.Interest.InterestHttp;

namespace MyWealthV2.Application.FunctionalTests.Interest;

/// <summary>Interest-run R20, R33: an account closure pays the interest accrued up to the day before, and a closing
/// account is not accrued.</summary>
[Parallelizable]
public class AccountClosureInterestTests : TestBase
{
    private static DateOnly TenDaysAgo => Today.AddDays(-10);

    /// <summary>A Custodial account funded with 5,000.00 USD ten days ago, with a 3 % schedule from then.</summary>
    private static async Task<(NominatedWorld World, Guid Account)> FundedTenDaysAgoAsync(bool schedule = true)
    {
        var world = await NominatedWorld.CreateAsync();
        if (schedule)
        {
            await InsertScheduleAsync(world.Tenant.Id, "USD", TenDaysAgo);
        }

        var account = await OpenCustodialAsync(world.Tenant.Id, world.AdviserToken.AccessToken, world.Customer.PublicId);
        (await CustodialHttp.FundAsync(world.AdviserToken.AccessToken, account, 5000m)).IsSuccessStatusCode.ShouldBeTrue();
        await BackdateAsync(account, TenDaysAgo);
        return (world, account);
    }

    [Test]
    public async Task ARequest_PostsTheFinalInterestFirst_AndThePayoutIncludesTheNetInterest()
    {
        var (world, account) = await FundedTenDaysAgoAsync();
        var payee = await CustodialHttp.VerifiedPayeeAsync(account);
        var gross = Round2(0.41095890m * 10);
        var tax = Round2(gross * 0.45m);
        var cash = 5000m + gross - tax;

        var closure = await AccountClosureHttp.RequestAsync(world.AdviserToken.AccessToken, account, payee);

        var posting = (await PostingsAsync(account)).ShouldHaveSingleItem();
        (posting.InterestDetail!.PeriodFrom, posting.InterestDetail.PeriodTo, posting.InterestDetail.Days)
            .ShouldBe((TenDaysAgo, Today.AddDays(-1), 10));
        posting.InterestDetail.GrossAmount.ShouldBe(gross);
        posting.SourceKind.ShouldBe(TransactionSourceKind.InterestRun);
        var run = (await RunsAsync(world.Tenant.Id)).ShouldHaveSingleItem();
        run.PublicId.ShouldBe(posting.SourcePublicId!.Value);
        (run.Trigger, run.AsOfDate, run.RanByUserId, run.PostingCount).ShouldBe(
            (InterestRunTrigger.AccountClosure, Today.AddDays(-1), (int?)world.Adviser.Id, 1));
        run.AccountId.ShouldNotBeNull();
        (await RowsAsync(account)).ShouldAllBe(row => row.PostedTransactionId == posting.Id);
        (await AccruedThroughAsync(account)).ShouldBe(Today.AddDays(-1));

        var item = await AccountClosureHttp.GetAsync(world.AdviserToken.AccessToken, account, closure);
        item.GetProperty("cashBalanceAtRequest").GetDecimal().ShouldBe(cash);
        item.GetProperty("payoutAmount").GetDecimal().ShouldBe(cash);
        var runs = await GetAsync(world.TenantAdmin.AccessToken, $"{Runs}?trigger=AccountClosure");
        runs.GetProperty("items")[0].GetProperty("accountId").GetGuid().ShouldBe(account);
    }

    [Test]
    public async Task ARefusedRequest_PostsNothing()
    {
        var (world, account) = await FundedTenDaysAgoAsync();
        await CustodialHttp.RequestWithdrawalAsync(account, 100m);

        var response = await AccountClosureHttp.PostAsync(world.AdviserToken.AccessToken, account,
            AccountClosureHttp.Body(await CustodialHttp.VerifiedPayeeAsync(account)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostingsAsync(account)).ShouldBeEmpty();
        (await RowsAsync(account)).ShouldBeEmpty();
        (await AccruedThroughAsync(account)).ShouldBeNull();
        (await RunsAsync(world.Tenant.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task AClosingAccount_IsSkippedByARun_AndAfterTheWithdrawalIsRejected_TheNextRunAccruesTheDaysInBetween()
    {
        var (world, account) = await FundedTenDaysAgoAsync();
        var closure = await AccountClosureHttp.RequestAsync(world.AdviserToken.AccessToken, account,
            await CustodialHttp.VerifiedPayeeAsync(account));

        await RunAsync(world.Tenant.Id, Today.AddDays(3));
        (await AccruedThroughAsync(account)).ShouldBe(Today.AddDays(-1));

        var withdrawal = await AccountClosureHttp.WithdrawalIdAsync(world.TenantAdmin.AccessToken, account, closure);
        var reject = await WithdrawalHttp.ActAsync(world.TenantAdmin.AccessToken, withdrawal, "reject", "Customer changed their mind");
        reject.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reject.Content.ReadAsStringAsync());
        await RunAsync(world.Tenant.Id, Today.AddDays(3));

        (await AccruedThroughAsync(account)).ShouldBe(Today.AddDays(3));
        // The days from today were accrued by the second run. When today + 3 is a month end, that run also posts the
        // month, so they are read by date, not by being unposted.
        (await RowsAsync(account)).Where(row => row.Date >= Today).Select(row => row.Date)
            .ShouldBe(Enumerable.Range(0, 4).Select(Today.AddDays));
    }

    [Test]
    public async Task AnAccountWithNoAccrual_StoresNoRun()
    {
        var (world, account) = await FundedTenDaysAgoAsync(schedule: false);

        await AccountClosureHttp.RequestAsync(world.AdviserToken.AccessToken, account, await CustodialHttp.VerifiedPayeeAsync(account));

        (await RunsAsync(world.Tenant.Id)).ShouldBeEmpty();
        (await PostingsAsync(account)).ShouldBeEmpty();
        (await AccruedThroughAsync(account)).ShouldBe(Today.AddDays(-1));
    }
}
