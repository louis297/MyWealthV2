using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Orders;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>Orders R20–R22: the send step and the cancel step of the broker sync, through a fake broker orders port.</summary>
[Parallelizable]
public class SendBrokerOrdersTests : TestBase
{
    private static InMemoryBrokerOrders Broker => BrokerHttp.Orders;

    [Test]
    public async Task APlacedOrder_IsSentOnce_WithItsIdAsTheClientOrderId_AndStoredDataOnly()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);

        var outcome = await BrokerHttp.SendAsync();
        await BrokerHttp.SendAsync();

        outcome.ShouldBe(new BrokerStepOutcome(1, 0));
        var (connection, request) = Broker.Placed.ShouldHaveSingleItem();
        connection.ShouldBe("simulator");
        request.ShouldBe(new BrokerOrderRequest(
            id.ToString(), "ACME", "USD", OrderSide.Buy, 10m, OrderType.Market, TimeInForce.Day, 105m, 100m));
        // R20: the call carries no Tenant, Account or Customer.
        typeof(BrokerOrderRequest).GetProperties().Select(property => property.Name).ShouldBe([
            "ClientOrderId", "Symbol", "Currency", "Side", "Quantity", "Type", "TimeInForce", "PriceLimit", "ReferencePrice"
        ], ignoreOrder: true);
    }

    [Test]
    public async Task Accepted_MakesTheOrderWorking_WithTheBrokersId_AndKeepsItsReservation()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        Broker.Answer(BrokerPlaceResult.Accepted("w1-O4"));
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await BrokerHttp.SendAsync();

        var item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Working");
        item.GetProperty("reservedCash").GetDecimal().ShouldBe(1055.25m);
        var send = item.GetProperty("send");
        send.GetProperty("attempts").GetInt32().ShouldBe(1);
        send.GetProperty("lastAttemptAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        send.GetProperty("acceptedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        send.GetProperty("lastFailure").ValueKind.ShouldBe(JsonValueKind.Null);
        (await OrderHttp.OrderAsync(id)).BrokerOrderId.ShouldBe("w1-O4");
    }

    [TestCase(OrderRejectReason.UnknownInstrument)]
    [TestCase(OrderRejectReason.InvalidOrder)]
    [TestCase(OrderRejectReason.MarketClosed)]
    [TestCase(OrderRejectReason.Refused)]
    public async Task Rejected_MakesTheOrderRejected_WithTheReason_AndReleasesTheReservation(OrderRejectReason reason)
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        Broker.Answer(BrokerPlaceResult.Rejected(reason));

        await BrokerHttp.SendAsync();
        await BrokerHttp.SendAsync();

        Broker.Placed.Count.ShouldBe(1);
        var item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Rejected");
        item.GetProperty("rejectReason").GetString().ShouldBe(reason.ToString());
        item.GetProperty("closedAt").ValueKind.ShouldBe(JsonValueKind.String);
        item.GetProperty("reservedCash").GetDecimal().ShouldBe(0m);
        (await world.CashAsync()).ShouldBe((5000.00m, 0m, 5000.00m));
    }

    [TestCase(BrokerFailure.Unreachable)]
    [TestCase(BrokerFailure.Timeout)]
    [TestCase(BrokerFailure.Unauthorized)]
    [TestCase(BrokerFailure.Unavailable)]
    [TestCase(BrokerFailure.BadResponse)]
    public async Task AFailedCall_LeavesTheOrderPlaced_AndTheNextRunSendsTheSameClientOrderIdAgain(BrokerFailure failure)
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        Broker.Answer(BrokerPlaceResult.Failed(failure));

        var first = await BrokerHttp.SendAsync();

        first.ShouldBe(new BrokerStepOutcome(1, 1));
        var item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Placed");
        item.GetProperty("send").GetProperty("attempts").GetInt32().ShouldBe(1);
        item.GetProperty("send").GetProperty("lastFailure").GetString().ShouldBe(failure.ToString());
        item.GetProperty("reservedCash").GetDecimal().ShouldBe(1055.25m);

        var second = await BrokerHttp.SendAsync();

        second.ShouldBe(new BrokerStepOutcome(1, 0));
        Broker.Placed.Select(call => call.Order).ShouldBe([Broker.Placed[0].Order, Broker.Placed[0].Order]);
        Broker.Placed[1].Order.ClientOrderId.ShouldBe(id.ToString());
        item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Working");
        item.GetProperty("send").GetProperty("attempts").GetInt32().ShouldBe(2);
        item.GetProperty("send").GetProperty("lastFailure").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task OneOrdersFailure_DoesNotStopTheOthers_AndTheyAreSentOldestFirst()
    {
        var world = await OrderWorld.CreateAsync();
        var first = await world.PlaceAsync("Buy", 1m);
        var second = await world.PlaceAsync("Buy", 2m);
        var third = await world.PlaceAsync("Buy", 3m);
        Broker.Answer(BrokerPlaceResult.Failed(BrokerFailure.Timeout));
        Broker.Answer(BrokerPlaceResult.Rejected(OrderRejectReason.InvalidOrder));

        var outcome = await BrokerHttp.SendAsync();

        outcome.ShouldBe(new BrokerStepOutcome(3, 1));
        Broker.Placed.Select(call => call.Order.ClientOrderId).ShouldBe([first.ToString(), second.ToString(), third.ToString()]);
        (await OrderHttp.OrderAsync(first)).Status.ShouldBe(OrderStatus.Placed);
        (await OrderHttp.OrderAsync(second)).Status.ShouldBe(OrderStatus.Rejected);
        (await OrderHttp.OrderAsync(third)).Status.ShouldBe(OrderStatus.Working);
    }

    [Test]
    public async Task ADisabledTenantsUnsentOrder_IsNotSent_AndOneWithAnAttemptIsSentAgain()
    {
        var world = await OrderWorld.CreateAsync();
        var attempted = await world.PlaceAsync("Buy", 1m);
        Broker.Answer(BrokerPlaceResult.Failed(BrokerFailure.Unavailable));
        await BrokerHttp.SendAsync();
        var unsent = await world.PlaceAsync("Buy", 2m);
        await TenantDisabler.DisableAsync(world.Tenant.Id);

        await BrokerHttp.SendAsync();

        Broker.Placed.Select(call => call.Order.ClientOrderId).ShouldBe([attempted.ToString(), attempted.ToString()]);
        (await OrderHttp.OrderAsync(attempted)).Status.ShouldBe(OrderStatus.Working);
        var waiting = await OrderHttp.OrderAsync(unsent);
        (waiting.Status, waiting.Attempts).ShouldBe((OrderStatus.Placed, 0));
    }

    [Test]
    public async Task AnOrderOfAnotherConnection_IsNotSent_AndAConnectionThatIsNotConfiguredSendsNothing()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 1m);
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Orders] SET [Connection] = N'other' WHERE [PublicId] = {id}");
        }

        (await BrokerHttp.SendAsync()).ShouldBe(BrokerStepOutcome.None);
        (await BrokerHttp.SendAsync("other")).ShouldBe(BrokerStepOutcome.None);

        Broker.Placed.ShouldBeEmpty();
        (await OrderHttp.OrderAsync(id)).Attempts.ShouldBe(0);
    }

    [Test]
    public async Task AnOrderAReportMovedOnWhileTheCallWasOut_KeepsItsStatus_AndTakesTheBrokersId()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        Broker.Answer(BrokerPlaceResult.Accepted("w1-O9"));
        Broker.WhilePlacing = async _ =>
        {
            // A fill of 4 arrives and is stored before the Accepted answer is.
            using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var report = ExecutionReport.Receive(
                "simulator", "w1-X1", ExecutionReportKind.Fill, id.ToString(), "w1-O9", 4m, 100.12m, OrderHttp.Today,
                OrderHttp.Today.AddDays(2), "CN-1", null, null, null, DateTimeOffset.UtcNow);
            db.ExecutionReports.Add(report);
            await db.SaveChangesAsync();
            var order = await db.Orders.IgnoreQueryFilters().Include(row => row.Fills).SingleAsync(row => row.PublicId == id);
            var schedule = await db.TradeFeeSchedules.IgnoreQueryFilters().Include(row => row.BandRows)
                .SingleAsync(row => row.Id == order.TradeFeeScheduleId);
            order.AddFill(
                report.Id, 4m, 100.12m, OrderHttp.Today, OrderHttp.Today.AddDays(2), "CN-1", DateTimeOffset.UtcNow,
                new OrderFeeTerms(schedule, 2));
            await db.SaveChangesAsync();
        };

        var outcome = await BrokerHttp.SendAsync();

        outcome.ShouldBe(new BrokerStepOutcome(1, 0));
        var stored = await OrderHttp.OrderAsync(id);
        (stored.Status, stored.BrokerOrderId, stored.FilledQuantity).ShouldBe((OrderStatus.PartlyFilled, "w1-O9", 4m));
    }

    [Test]
    public async Task AnOrderWithACancelRequest_IsNeverSent_AndEndsWhenTheBrokerDoesNotKnowIt()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        Broker.Answer(BrokerPlaceResult.Failed(BrokerFailure.Timeout));
        await BrokerHttp.SendAsync();
        (await OrderHttp.CancelAsync(world.Adviser, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Broker.Answer(BrokerCancelResult.Answered(BrokerCancelAnswer.UnknownOrder));
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var sent = await BrokerHttp.SendAsync();
        var cancelled = await BrokerHttp.CancelAsync();
        await BrokerHttp.CancelAsync();

        sent.ShouldBe(BrokerStepOutcome.None);
        cancelled.ShouldBe(new BrokerStepOutcome(1, 0));
        Broker.Placed.Count.ShouldBe(1);
        Broker.Cancels.ShouldBe([("simulator", id.ToString())]);
        var item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Cancelled");
        item.GetProperty("closeReason").GetString().ShouldBe("Not at the broker");
        item.GetProperty("closedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        item.GetProperty("reservedCash").GetDecimal().ShouldBe(0m);
        (await world.CashAsync()).Reserved.ShouldBe(0m);
    }

    [TestCase(BrokerCancelAnswer.Accepted)]
    [TestCase(BrokerCancelAnswer.TooLate)]
    public async Task AnAnsweredCancel_MarksTheRequestAnswered_ChangesNothingElse_AndIsNotAskedAgain(BrokerCancelAnswer answer)
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        (await OrderHttp.CancelAsync(world.Adviser, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Broker.Answer(BrokerCancelResult.Answered(answer));

        var outcome = await BrokerHttp.CancelAsync();
        await BrokerHttp.CancelAsync();

        outcome.ShouldBe(new BrokerStepOutcome(1, 0));
        Broker.Cancels.Count.ShouldBe(1);
        var stored = await OrderHttp.OrderAsync(id);
        stored.Status.ShouldBe(OrderStatus.Working);
        stored.CancelAnsweredAt.ShouldNotBeNull();
        stored.ReservedCash.ShouldBe(1055.25m);
        stored.ClosedAt.ShouldBeNull();
    }

    [Test]
    public async Task AFailedCancelCall_LeavesTheRequestUnanswered_AndTheNextRunAsksAgain()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        (await OrderHttp.CancelAsync(world.Adviser, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Broker.Answer(BrokerCancelResult.Failed(BrokerFailure.Unreachable));

        var first = await BrokerHttp.CancelAsync();

        first.ShouldBe(new BrokerStepOutcome(1, 1));
        (await OrderHttp.OrderAsync(id)).CancelAnsweredAt.ShouldBeNull();

        var second = await BrokerHttp.CancelAsync();

        second.ShouldBe(new BrokerStepOutcome(1, 0));
        Broker.Cancels.Count.ShouldBe(2);
        (await OrderHttp.OrderAsync(id)).CancelAnsweredAt.ShouldNotBeNull();
    }
}
