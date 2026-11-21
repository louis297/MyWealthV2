using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.BankFeed;

/// <summary>Withdrawals R32–R34: the send step of the bank statement sync job, through a fake bank payments port.</summary>
[Parallelizable]
public class SendBankPaymentsTests : TestBase
{
    [Test]
    public async Task AnApprovedWithdrawal_IsSentOnce_WithItsIdAsTheInstruction_AndStoredData()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.ApprovedAsync(300m);

        await BankFeedHttp.SendAsync();

        var request = Payments.Requests.ShouldHaveSingleItem();
        request.Connection.ShouldBe(BankFeedHttp.Connection);
        request.TrustAccountNumber.ShouldBe(world.ClientMoney.AccountNumber);
        request.InstructionId.ShouldBe(id);
        request.Amount.ShouldBe(300m);
        request.Currency.ShouldBe("USD");
        request.Reference.ShouldBe(world.DepositReference);
        request.Payee.ShouldBe(new BankPaymentPayee("06-0123-0456789-00", "J Smith"));
    }

    [Test]
    public async Task Accepted_MakesTheWithdrawalSent_AndKeepsThePaymentId()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.ApprovedAsync(300m);
        Payments.Answer(BankPaymentResult.Accepted("w1-P4"));
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await BankFeedHttp.SendAsync();
        await BankFeedHttp.SendAsync();

        Payments.Requests.Count.ShouldBe(1);
        var item = await WithdrawalHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Sent");
        var payment = item.GetProperty("payment");
        payment.GetProperty("status").GetString().ShouldBe("Accepted");
        payment.GetProperty("attempts").GetInt32().ShouldBe(1);
        payment.GetProperty("lastAttemptAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        payment.GetProperty("sentAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        (await BankPaymentIdAsync(id)).ShouldBe("w1-P4");
        (await world.CashAsync()).ShouldBe((1000m, 300m, 700m));
    }

    [Test]
    public async Task Rejected_MakesTheWithdrawalFailed_WithTheReason_AndReleasesTheReservation()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.ApprovedAsync(300m);
        Payments.Answer(BankPaymentResult.Rejected(PaymentRejectReason.AccountClosed));

        await BankFeedHttp.SendAsync();

        var item = await WithdrawalHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Failed");
        item.GetProperty("failureReason").GetString().ShouldBe("AccountClosed");
        item.GetProperty("payment").GetProperty("status").GetString().ShouldBe("Rejected");
        (await world.CashAsync()).ShouldBe((1000m, 0m, 1000m));
        await BankFeedHttp.SendAsync();
        Payments.Requests.Count.ShouldBe(1);
    }

    [TestCase(BankFeedFailure.Unreachable)]
    [TestCase(BankFeedFailure.Timeout)]
    [TestCase(BankFeedFailure.Unauthorized)]
    [TestCase(BankFeedFailure.Unavailable)]
    [TestCase(BankFeedFailure.UnknownAccount)]
    [TestCase(BankFeedFailure.BadResponse)]
    public async Task AFailedCall_LeavesItApproved_AndTheNextRunSendsItAgainWithTheSameInstruction(BankFeedFailure failure)
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.ApprovedAsync(300m);
        Payments.Answer(BankPaymentResult.Failed(failure));

        await BankFeedHttp.SendAsync();

        var item = await WithdrawalHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Approved");
        item.GetProperty("payment").GetProperty("status").GetString().ShouldBe("Pending");
        item.GetProperty("payment").GetProperty("attempts").GetInt32().ShouldBe(1);
        item.GetProperty("payment").GetProperty("lastFailure").GetString().ShouldBe(failure.ToString());

        await BankFeedHttp.SendAsync();

        Payments.Requests.Select(request => request.InstructionId).ShouldBe([id, id]);
        var after = await WithdrawalHttp.GetAsync(world.TenantAdmin, id);
        after.GetProperty("status").GetString().ShouldBe("Sent");
        after.GetProperty("payment").GetProperty("attempts").GetInt32().ShouldBe(2);
        after.GetProperty("payment").GetProperty("lastFailure").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task OnePaymentsFailure_DoesNotStopTheOthers()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var first = await world.ApprovedAsync(100m);
        var second = await world.ApprovedAsync(200m);
        Payments.Answer(BankPaymentResult.Failed(BankFeedFailure.Timeout));

        await BankFeedHttp.SendAsync();

        Payments.Requests.Select(request => request.InstructionId).ShouldBe([first, second]);
        (await WithdrawalHttp.GetAsync(world.TenantAdmin, first)).GetProperty("status").GetString().ShouldBe("Approved");
        (await WithdrawalHttp.GetAsync(world.TenantAdmin, second)).GetProperty("status").GetString().ShouldBe("Sent");
    }

    [Test]
    public async Task ADisabledTenant_GetsNoFirstAttempt_ButAnAttemptedPaymentIsSentAgain()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var attempted = await world.ApprovedAsync(100m);
        Payments.Answer(BankPaymentResult.Failed(BankFeedFailure.Timeout));
        await BankFeedHttp.SendAsync();
        var fresh = await world.ApprovedAsync(200m);
        await TenantDisabler.DisableAsync(world.Tenant.Id);

        await BankFeedHttp.SendAsync();

        Payments.Requests.Select(request => request.InstructionId).ShouldBe([attempted, attempted]);
        (await PaymentStatusAsync(fresh)).ShouldBe((OutgoingPaymentStatus.Pending, 0));
    }

    [Test]
    public async Task ARequestedOrCancelledWithdrawal_OrAnInactiveClientMoneyAccount_IsNotSent()
    {
        var world = await WithdrawalWorld.CreateAsync();
        await world.RequestAsync(10m);
        var cancelled = await world.ApprovedAsync(20m);
        (await WithdrawalHttp.ActAsync(world.Adviser, cancelled, "cancel")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var waiting = await world.ApprovedAsync(30m);
        await SetClientMoneyActiveAsync(world.ClientMoney.Id, active: false);

        await BankFeedHttp.SendAsync();

        Payments.Requests.ShouldBeEmpty();
        (await PaymentStatusAsync(waiting)).ShouldBe((OutgoingPaymentStatus.Pending, 0));
    }

    private static InMemoryBankPayments Payments => BankFeedHttp.Payments;

    private static async Task<string?> BankPaymentIdAsync(Guid withdrawalId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.OutgoingPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.InstructionPublicId == withdrawalId).Select(row => row.BankPaymentId).SingleAsync();
    }

    private static async Task<(OutgoingPaymentStatus, int)> PaymentStatusAsync(Guid withdrawalId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.OutgoingPayments.IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.InstructionPublicId == withdrawalId);
        return (row.Status, row.Attempts);
    }

    private static async Task SetClientMoneyActiveAsync(int id, bool active)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.ClientMoneyAccounts.IgnoreQueryFilters().SingleAsync(item => item.Id == id);
        if (active)
        {
            row.Enable();
        }
        else
        {
            row.Disable();
        }

        await db.SaveChangesAsync();
    }
}
