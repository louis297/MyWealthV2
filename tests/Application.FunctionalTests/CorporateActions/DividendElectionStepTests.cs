using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Application.FunctionalTests.InKindTransfers;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionRuns;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Dividends R21–R23, R29: the custodian job sends the reinvestment election of a dividend.</summary>
[Parallelizable]
public class DividendElectionStepTests : TestBase
{
    private static InMemoryCustodianElections Custodian =>
        FunctionalTestSetup.WebApiServices.GetRequiredService<InMemoryCustodianElections>();

    private static async Task<CustodianSendOutcome> SendAsync()
    {
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SendReinvestmentElections>().RunAsync(SafekeepingHttp.Connection);
    }

    private static Task<DividendWorld> PlanAsync() =>
        DividendWorld.CreateAsync(price: 25m, setUp: async (world, holders) =>
        {
            await SafekeepingHttp.EnsureActiveAsync(world.Tenant.Id, "USD");
            await DividendReinvestmentTests.ElectAsync(world, holders.Ned);
        });

    [Test]
    public async Task ThePendingElection_IsSentOnce_WithTheCorporateActionAsInstruction_AndAcceptedLowersTheExpectedAmount()
    {
        var dividend = await PlanAsync();
        var action = await dividend.DividendAsync();

        (await SendAsync()).Sent.ShouldBe(1);
        (await SendAsync()).Sent.ShouldBe(0);

        var request = Custodian.Requests.ShouldHaveSingleItem();
        request.InstructionId.ShouldBe(dividend.DividendId);
        (request.Connection, request.CorporateActionId, request.Symbol, request.Currency, request.Quantity)
            .ShouldBe((SafekeepingHttp.Connection, "w-CA3", "ACME.SIM", "USD", 78.82352941m));
        request.AccountNumber.ShouldNotBeNullOrEmpty();
        var accepted = await dividend.DividendAsync();
        (accepted.ElectionStatus, accepted.ElectionAttempts).ShouldBe(((ReinvestmentElectionStatus?)ReinvestmentElectionStatus.Accepted, 1));
        accepted.ElectionLastAttemptAt.ShouldNotBeNull();
        action.ElectionAttempts.ShouldBe(0);

        // 78.82352941 shares × 0.85 = 67.00 is reinvested: 93.50 − 67.00.
        var item = await ItemAsync(dividend.TenantAdmin, dividend.DividendId);
        item.GetProperty("payment").GetProperty("expectedAmount").GetDecimal().ShouldBe(26.50m);
        item.GetProperty("election").GetProperty("status").GetString().ShouldBe("Accepted");
        item.GetProperty("election").GetProperty("attempts").GetInt32().ShouldBe(1);
    }

    [Test]
    public async Task WithAnAcceptedElection_TheReceiptAloneDoesNotPay()
    {
        var dividend = await PlanAsync();
        await SendAsync();

        var line = await dividend.ReceiveAsync(26.50m);

        line.MatchMethod.ShouldBe(StatementLineMatchMethod.Dividend);
        (await dividend.DividendAsync()).PaidAt.ShouldBeNull();
    }

    [Test]
    public async Task ARejectedElection_IsPaidInCash()
    {
        var dividend = await PlanAsync();
        Custodian.Answer(CustodianElectionResult.Rejected(ElectionRejectReason.TooLate));
        await SendAsync();
        var rejected = await dividend.DividendAsync();
        (rejected.ElectionStatus, rejected.ElectionReason).ShouldBe(
            ((ReinvestmentElectionStatus?)ReinvestmentElectionStatus.Rejected, (ElectionRejectReason?)ElectionRejectReason.TooLate));
        (await ItemAsync(dividend.TenantAdmin, dividend.DividendId)).GetProperty("election").GetProperty("reason").GetString()
            .ShouldBe("TooLate");

        await dividend.ReceiveAsync(93.50m);
        (await RunApplyAsync()).Posted.ShouldBe(2);

        (await EntitlementsAsync(dividend.DividendId)).ShouldAllBe(row => row.ReinvestedQuantity == null);
    }

    [Test]
    public async Task AFailedCall_LeavesItPending_AndTheNextRunSendsTheSameElection()
    {
        var dividend = await PlanAsync();
        Custodian.Answer(CustodianElectionResult.Failed(CustodianFailure.Unavailable));

        (await SendAsync()).Failed.ShouldBe(1);
        var pending = await dividend.DividendAsync();
        (pending.ElectionStatus, pending.ElectionAttempts).ShouldBe(((ReinvestmentElectionStatus?)ReinvestmentElectionStatus.Pending, 1));

        await SendAsync();

        Custodian.Requests.Count.ShouldBe(2);
        Custodian.Requests[1].ShouldBe(Custodian.Requests[0]);
        (await dividend.DividendAsync()).ElectionStatus.ShouldBe(ReinvestmentElectionStatus.Accepted);
    }

    [Test]
    public async Task AnElectionWhoseDividendAlreadyHasItsReceipt_Lapses_IsNotSent_AndTheDividendIsPaidInCash()
    {
        var dividend = await PlanAsync();
        await dividend.ReceiveAsync(93.50m);

        await SendAsync();

        Custodian.Requests.ShouldBeEmpty();
        var lapsed = await dividend.DividendAsync();
        lapsed.ElectionStatus.ShouldBe(ReinvestmentElectionStatus.Lapsed);
        lapsed.PaidAt.ShouldNotBeNull();
    }

    [Test]
    public async Task AnElectionWhoseSafekeepingAccountIsNoLongerActive_Lapses()
    {
        var dividend = await PlanAsync();
        await DeactivateSafekeepingAsync(dividend.World.Tenant.Id);

        await SendAsync();

        Custodian.Requests.ShouldBeEmpty();
        (await dividend.DividendAsync()).ElectionStatus.ShouldBe(ReinvestmentElectionStatus.Lapsed);
    }

    private static async Task DeactivateSafekeepingAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyWealthV2.Infrastructure.Data.ApplicationDbContext>();
        foreach (var row in await db.SafekeepingAccounts.IgnoreQueryFilters().Where(row => row.TenantId == tenantId).ToListAsync())
        {
            row.Disable();
        }

        await db.SaveChangesAsync();
    }
}
