using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.Interest.InterestDb;
using static MyWealthV2.Application.FunctionalTests.Interest.InterestHttp;

namespace MyWealthV2.Application.FunctionalTests.Interest;

/// <summary>Interest-run R16–R22, R24–R28: accrue one account through a day and post, through a direct call of the
/// service (§12 commit 6).</summary>
[Parallelizable]
public class AccountInterestTests : TestBase
{
    private const decimal FiveThousandAt3 = 0.41095890m;

    private static async Task<(NominatedWorld World, Guid Account, Guid Schedule)> FundedAsync(decimal amount = 5000m)
    {
        var world = await NominatedWorld.CreateAsync();
        var schedule = await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", NextMonth);
        var account = await OpenCustodialAsync(world.Tenant.Id, world.AdviserToken.AccessToken, world.Customer.PublicId);
        (await CustodialHttp.FundAsync(world.AdviserToken.AccessToken, account, amount)).IsSuccessStatusCode.ShouldBeTrue();
        return (world, account, schedule);
    }

    [Test]
    public async Task Accrue_StoresOneRowPerDay_FromTheSchedulesFirstDay_OnEachDaysClosingBalance()
    {
        var (world, account, schedule) = await FundedAsync();
        var scheduleId = await ScheduleInternalIdAsync(schedule);

        (await AccrueAndPostAsync(account, NextMonth.AddDays(9), NextMonth.AddDays(-1))).ShouldBe((10, null));
        (await CustodialHttp.FundAsync(world.AdviserToken.AccessToken, account, 1000m)).IsSuccessStatusCode.ShouldBeTrue();
        (await AccrueAndPostAsync(account, NextMonth.AddDays(19), NextMonth.AddDays(-1))).ShouldBe((10, null));

        var rows = await RowsAsync(account);
        rows.Select(row => row.Date).ShouldBe(Enumerable.Range(0, 20).Select(NextMonth.AddDays));
        rows.Take(10).ShouldAllBe(row => row.Balance == 5000m && row.Amount == FiveThousandAt3 && row.EffectiveRatePercent == 3m);
        rows.Skip(10).ShouldAllBe(row => row.Balance == 6000m && row.Amount == 0.49315068m);
        rows.ShouldAllBe(row => row.InterestRateScheduleId == scheduleId && row.PostedTransactionId == null);
        (await AccruedThroughAsync(account)).ShouldBe(NextMonth.AddDays(19));
    }

    [Test]
    public async Task AtTheMonthEnd_OneInterestIsPosted_WithPrincipalTaxBothDetails_AndTheRowsAreStamped()
    {
        var (world, account, _) = await FundedAsync();
        await TaxProfileHttp.SetAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "NewZealand", true, 17.5m);
        var end = MonthEnd(NextMonth);
        var days = end.DayNumber - NextMonth.DayNumber + 1;
        var accrued = FiveThousandAt3 * days;
        var gross = Round2(accrued);
        var tax = Round2(gross * 0.175m);
        var runId = Guid.NewGuid();

        var (added, postingId) = await AccrueAndPostAsync(account, end, end, runId);

        added.ShouldBe(days);
        var posting = (await PostingsAsync(account)).ShouldHaveSingleItem();
        posting.PublicId.ShouldBe(postingId!.Value);
        posting.Memo.ShouldBe("Interest");
        posting.Reference.ShouldBeNull();
        posting.SourceKind.ShouldBe(TransactionSourceKind.InterestRun);
        posting.SourcePublicId.ShouldBe(runId);
        posting.CashLegs.Single(leg => leg.Role == CashLegRole.Principal).Amount.ShouldBe(gross);
        var taxLeg = posting.CashLegs.Single(leg => leg.Role == CashLegRole.Tax);
        taxLeg.Amount.ShouldBe(-tax);
        (taxLeg.TaxDetail!.Kind, taxLeg.TaxDetail.RatePercent, taxLeg.TaxDetail.BasisAmount, taxLeg.TaxDetail.TaxAmount)
            .ShouldBe((TaxKind.Rwt, 17.5m, gross, tax));
        var detail = posting.InterestDetail!;
        (detail.PeriodFrom, detail.PeriodTo, detail.Days, detail.AccruedAmount, detail.GrossAmount)
            .ShouldBe((NextMonth, end, days, accrued, gross));
        (await RowsAsync(account)).ShouldAllBe(row => row.PostedTransactionId == posting.Id);
        (await CustodialHttp.CashBalanceAsync(world.AdviserToken.AccessToken, account)).ShouldBe(5000m + gross - tax);
    }

    [Test]
    public async Task TheSameCallAgain_AddsNothing_AndPostsNothing()
    {
        var (_, account, _) = await FundedAsync();
        var end = MonthEnd(NextMonth);
        await AccrueAndPostAsync(account, end, end);

        (await AccrueAndPostAsync(account, end, end)).ShouldBe((0, null));
        (await AccrueAndPostAsync(account, end.AddDays(-3), end)).ShouldBe((0, null));

        (await PostingsAsync(account)).Count.ShouldBe(1);
        (await AccruedThroughAsync(account)).ShouldBe(end);
    }

    [Test]
    public async Task AMidMonthDay_StoresRows_AndPostsNothing()
    {
        var (_, account, _) = await FundedAsync();

        (await AccrueAndPostAsync(account, NextMonth.AddDays(14), NextMonth.AddDays(-1))).ShouldBe((15, null));

        (await PostingsAsync(account)).ShouldBeEmpty();
    }

    [Test]
    public async Task TwoMissedMonths_ArePostedAsOneTransaction()
    {
        var (_, account, _) = await FundedAsync();
        var end = MonthEnd(NextMonth.AddMonths(1));
        var days = end.DayNumber - NextMonth.DayNumber + 1;

        await AccrueAndPostAsync(account, end, end);

        var posting = (await PostingsAsync(account)).ShouldHaveSingleItem();
        (posting.InterestDetail!.PeriodFrom, posting.InterestDetail.PeriodTo, posting.InterestDetail.Days)
            .ShouldBe((NextMonth, end, days));
        posting.InterestDetail.GrossAmount.ShouldBe(Round2(FiveThousandAt3 * days));
    }

    [Test]
    public async Task ABalanceTooSmallToRoundAbove0_PostsNothing_AndTheRowsCountInTheNextPosting()
    {
        var (_, account, _) = await FundedAsync(1.50m);
        var first = MonthEnd(NextMonth);
        var second = MonthEnd(NextMonth.AddMonths(1));

        (await AccrueAndPostAsync(account, first, first)).PostingId.ShouldBeNull();
        (await RowsAsync(account)).ShouldAllBe(row => row.PostedTransactionId == null);

        (await AccrueAndPostAsync(account, second, second)).PostingId.ShouldNotBeNull();
        var posting = (await PostingsAsync(account)).ShouldHaveSingleItem();
        posting.InterestDetail!.PeriodFrom.ShouldBe(NextMonth);
        posting.InterestDetail.GrossAmount.ShouldBe(0.01m);
        posting.CashLegs.ShouldHaveSingleItem().Role.ShouldBe(CashLegRole.Principal);
    }

    [Test]
    public async Task AScheduleChangeMidMonth_AccruesEachDayAtItsOwnSchedule()
    {
        var (world, account, first) = await FundedAsync();
        var second = await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", NextMonth.AddDays(10), Band(0m, 2m));

        await AccrueAndPostAsync(account, NextMonth.AddDays(19), NextMonth.AddDays(-1));

        var rows = await RowsAsync(account);
        var firstId = await ScheduleInternalIdAsync(first);
        var secondId = await ScheduleInternalIdAsync(second);
        rows.Take(10).ShouldAllBe(row => row.InterestRateScheduleId == firstId && row.Amount == FiveThousandAt3);
        rows.Skip(10).ShouldAllBe(row => row.InterestRateScheduleId == secondId && row.Amount == Daily(5000m, 2m)
                                          && row.EffectiveRatePercent == 2m);
    }

    [TestCase("NewZealand", true, 30.0, "Rwt", 30.0)]
    [TestCase(null, false, null, "Rwt", 45.0)]
    [TestCase("Overseas", false, null, "Nrwt", 15.0)]
    public async Task TheProfileAtThePosting_AppliesToTheWholePosting(
        string? residency, bool irdNumberHeld, double? chosen, string kind, double rate)
    {
        var (world, account, _) = await FundedAsync();
        await TaxProfileHttp.SetAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "NewZealand", true, 17.5m);
        await AccrueAndPostAsync(account, NextMonth.AddDays(14), NextMonth.AddDays(-1));
        if (residency is null)
        {
            await TaxProfileHttp.SetAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "NewZealand", false, null);
        }
        else
        {
            await TaxProfileHttp.SetAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, residency, irdNumberHeld,
                chosen is null ? null : (decimal)chosen);
        }

        var end = MonthEnd(NextMonth);
        await AccrueAndPostAsync(account, end, end);

        var posting = (await PostingsAsync(account)).ShouldHaveSingleItem();
        var gross = posting.InterestDetail!.GrossAmount;
        var detail = posting.CashLegs.Single(leg => leg.Role == CashLegRole.Tax).TaxDetail!;
        detail.Kind.ToString().ShouldBe(kind);
        detail.RatePercent.ShouldBe((decimal)rate);
        detail.TaxAmount.ShouldBe(Round2(gross * (decimal)rate / 100m));
    }

    [Test]
    public async Task ACustomerWithNoProfile_IsWithheldAt45()
    {
        var (_, account, _) = await FundedAsync();
        var end = MonthEnd(NextMonth);

        await AccrueAndPostAsync(account, end, end);

        var detail = (await PostingsAsync(account)).Single().CashLegs.Single(leg => leg.Role == CashLegRole.Tax).TaxDetail!;
        (detail.Kind, detail.RatePercent).ShouldBe((TaxKind.Rwt, 45m));
    }

    [Test]
    public async Task ReservedCash_StillEarns()
    {
        var (_, account, _) = await FundedAsync();
        await CustodialHttp.RequestWithdrawalAsync(account, 1000m);

        await AccrueAndPostAsync(account, NextMonth.AddDays(2), NextMonth.AddDays(-1));

        (await RowsAsync(account)).ShouldAllBe(row => row.Balance == 5000m);
    }

    [Test]
    public async Task AnAccountWithNoCash_StoresNoRow_ButIsAccruedThroughTheDay()
    {
        var world = await NominatedWorld.CreateAsync();
        await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", NextMonth);
        var account = await OpenCustodialAsync(world.Tenant.Id, world.AdviserToken.AccessToken, world.Customer.PublicId);

        (await AccrueAndPostAsync(account, NextMonth.AddDays(5), NextMonth.AddDays(-1))).ShouldBe((0, null));

        (await RowsAsync(account)).ShouldBeEmpty();
        (await AccruedThroughAsync(account)).ShouldBe(NextMonth.AddDays(5));
    }

    [Test]
    public async Task AnExternalAccount_IsLeftAsItIs()
    {
        var world = await NominatedWorld.CreateAsync();
        await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", NextMonth);
        var account = await CustodialHttp.OpenExternalAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);
        await CustodialHttp.FundAsync(world.AdviserToken.AccessToken, account, 5000m, external: true);

        (await AccrueAndPostAsync(account, NextMonth.AddDays(5), NextMonth.AddDays(-1))).ShouldBe((0, null));

        (await RowsAsync(account)).ShouldBeEmpty();
        (await AccruedThroughAsync(account)).ShouldBeNull();
    }

    [Test]
    public async Task TwoTenants_AnAccountEarnsOnlyFromItsOwnTenantsSchedule()
    {
        var world = await NominatedWorld.CreateAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");
        await CreateScheduleAsync(south.TenantAdmin.AccessToken, "USD", NextMonth);
        var account = await OpenCustodialAsync(world.Tenant.Id, world.AdviserToken.AccessToken, world.Customer.PublicId);
        await CustodialHttp.FundAsync(world.AdviserToken.AccessToken, account, 5000m);

        (await AccrueAndPostAsync(account, NextMonth.AddDays(5), NextMonth.AddDays(-1))).ShouldBe((0, null));

        (await RowsAsync(account)).ShouldBeEmpty();
    }
}
