using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>In-kind transfers R8, R10–R13: pull and store custody movements, the cursor and the custodian's positions.</summary>
[Parallelizable]
public class PullCustodyMovementsTests : TestBase
{
    [Test]
    public async Task AMovement_IsStoredOnce_Received_WithItsInstrument()
    {
        var world = await TransferWorld.CreateAsync();
        var number = world.Safekeeping.AccountNumber;
        var movement = TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "acme", 100m, instructionId: "abc");

        await TransferHttp.PullAsync();
        TransferHttp.Feed.Add(number, movement);
        await TransferHttp.PullAsync();

        var stored = (await TransferHttp.MovementsAsync(world.Safekeeping.Id)).ShouldHaveSingleItem();
        (stored.TenantId, stored.Connection, stored.OutsideId).ShouldBe((world.Tenant.Id, "simulator", movement.Id!));
        stored.Status.ShouldBe(CustodyMovementStatus.Received);
        (stored.Direction, stored.Kind, stored.Symbol, stored.Currency, stored.Quantity)
            .ShouldBe((CustodyMovementDirection.In, (CustodyMovementKind?)CustodyMovementKind.Transfer, "acme", "USD", 100m));
        stored.InstrumentId.ShouldBe(await InstrumentIdAsync(world.InstrumentId));
        (stored.InstructionId, stored.CounterpartyName, stored.CounterpartyAccount)
            .ShouldBe(("abc", "Harbour Brokers", "HB-0044172"));
        stored.ReceivedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Test]
    public async Task AMovementOfASymbolTheTenantDoesNotList_HasNoInstrument_AndAnUnknownKindIsStoredWithNone()
    {
        var world = await TransferWorld.CreateAsync();
        TransferHttp.Feed.AddMovement(world.Safekeeping.AccountNumber, CustodyMovementDirection.Out, "OTHER", 5m, kind: null);

        await TransferHttp.PullAsync();

        var stored = (await TransferHttp.MovementsAsync(world.Safekeeping.Id)).ShouldHaveSingleItem();
        (stored.InstrumentId, stored.Kind).ShouldBe(((int?)null, (CustodyMovementKind?)null));
    }

    [Test]
    public async Task ThePositions_AreStoredWithEveryPage_AndReplaced_AndPositionsAtMoves()
    {
        var world = await TransferWorld.CreateAsync();
        var number = world.Safekeeping.AccountNumber;
        TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "ACME", 100m);
        TransferHttp.Feed.Omit(number, new CustodianFeedMovement("omitted-1", CustodyMovementDirection.In, CustodyMovementKind.Transfer,
            "BETA", "USD", 7m, DateOnly.FromDateTime(DateTime.UtcNow)));

        await TransferHttp.PullAsync();
        var (first, firstPositions) = await TransferHttp.FeedStateAsync(world.Safekeeping.Id);
        TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.Out, "ACME", 100m);
        await TransferHttp.PullAsync();
        var (second, secondPositions) = await TransferHttp.FeedStateAsync(world.Safekeeping.Id);

        firstPositions.Select(position => (position.Symbol, position.Quantity)).ShouldBe([("ACME", 100m), ("BETA", 7m)]);
        secondPositions.Select(position => (position.Symbol, position.Quantity)).ShouldBe([("BETA", 7m)]);
        first!.PositionsAt.ShouldNotBeNull();
        second!.PositionsAt!.Value.ShouldBeGreaterThan(first.PositionsAt!.Value);
        second.Cursor.ShouldBe("3");
        (await TransferHttp.MovementsAsync(world.Safekeeping.Id)).Count.ShouldBe(2);
    }

    [Test]
    public async Task SeveralPages_AreAllStored()
    {
        var world = await TransferWorld.CreateAsync();
        TransferHttp.Feed.PageSize = 2;
        for (var i = 0; i < 5; i++)
        {
            TransferHttp.Feed.AddMovement(world.Safekeeping.AccountNumber, CustodyMovementDirection.In, "ACME", 1m);
        }

        await TransferHttp.PullAsync();

        (await TransferHttp.MovementsAsync(world.Safekeeping.Id)).Count.ShouldBe(5);
        TransferHttp.Feed.Requests.Select(request => request.Cursor).ShouldBe([null, "2", "4"]);
        TransferHttp.Feed.Requests.ShouldAllBe(request => request.Limit == 100);
    }

    [TestCase("quantity")]
    [TestCase("direction")]
    [TestCase("currency")]
    [TestCase("settlementDate")]
    [TestCase("id")]
    public async Task APageWithAMovementItCannotStore_StoresNothing_AndRecordsInvalidItem(string broken)
    {
        var world = await TransferWorld.CreateAsync();
        var number = world.Safekeeping.AccountNumber;
        TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "ACME", 1m);
        await TransferHttp.PullAsync();
        var (before, positionsBefore) = await TransferHttp.FeedStateAsync(world.Safekeeping.Id);
        var good = new CustodianFeedMovement("ok-1", CustodyMovementDirection.In, CustodyMovementKind.Transfer, "ACME", "USD", 1m,
            DateOnly.FromDateTime(DateTime.UtcNow));
        TransferHttp.Feed.Add(number, good with { Id = "ok-2" });
        TransferHttp.Feed.Add(number, broken switch
        {
            "quantity" => good with { Quantity = 0m },
            "direction" => good with { Direction = null },
            "currency" => good with { Currency = "NZD" },
            "settlementDate" => good with { SettlementDate = null },
            _ => good with { Id = null }
        });

        var outcome = await TransferHttp.PullAsync();

        outcome.FailedSafekeepingAccounts.ShouldBe([world.Safekeeping.PublicId]);
        (await TransferHttp.MovementsAsync(world.Safekeeping.Id)).Count.ShouldBe(1);
        var (after, positionsAfter) = await TransferHttp.FeedStateAsync(world.Safekeeping.Id);
        after!.Cursor.ShouldBe(before!.Cursor);
        after.LastFailure.ShouldBe(CustodianFailure.InvalidItem);
        positionsAfter.Select(position => position.Quantity).ShouldBe(positionsBefore.Select(position => position.Quantity));
    }

    [TestCase("none")]
    [TestCase("currency")]
    [TestCase("twice")]
    [TestCase("quantity")]
    public async Task APageWithPositionsItCannotRead_StoresNothing_AndRecordsInvalidItem(string broken)
    {
        var world = await TransferWorld.CreateAsync();
        var number = world.Safekeeping.AccountNumber;
        TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "ACME", 1m);
        TransferHttp.Feed.SetPositions(number, broken switch
        {
            "none" => null,
            "currency" => [new CustodianFeedPosition("ACME", "NZD", 1m)],
            "twice" => [new CustodianFeedPosition("ACME", "USD", 1m), new CustodianFeedPosition("acme", "USD", 1m)],
            _ => [new CustodianFeedPosition("ACME", "USD", null)]
        });

        await TransferHttp.PullAsync();

        (await TransferHttp.MovementsAsync(world.Safekeeping.Id)).ShouldBeEmpty();
        var (cursor, positions) = await TransferHttp.FeedStateAsync(world.Safekeeping.Id);
        cursor!.Cursor.ShouldBeNull();
        cursor.LastFailure.ShouldBe(CustodianFailure.InvalidItem);
        positions.ShouldBeEmpty();
    }

    [Test]
    public async Task AFailedCall_KeepsTheCursorAndThePositions_AndRecordsTheFailure()
    {
        var world = await TransferWorld.CreateAsync();
        var number = world.Safekeeping.AccountNumber;
        TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "ACME", 1m);
        await TransferHttp.PullAsync();
        TransferHttp.Feed.Fail(number, CustodianFailure.Timeout);
        TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "ACME", 1m);

        await TransferHttp.PullAsync();

        var (cursor, positions) = await TransferHttp.FeedStateAsync(world.Safekeeping.Id);
        (cursor!.Cursor, cursor.LastFailure).ShouldBe(("1", (CustodianFailure?)CustodianFailure.Timeout));
        cursor.LastFailedAt.ShouldNotBeNull();
        positions.ShouldHaveSingleItem().Quantity.ShouldBe(1m);
    }

    [Test]
    public async Task AnInactiveSafekeepingAccount_IsNotRead_AndAnotherConnectionReadsNothing()
    {
        var world = await TransferWorld.CreateAsync();
        TransferHttp.Feed.AddMovement(world.Safekeeping.AccountNumber, CustodyMovementDirection.In, "ACME", 1m);
        await TransferHttp.PullAsync("other");
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.SafekeepingAccounts.IgnoreQueryFilters().SingleAsync(row => row.Id == world.Safekeeping.Id)).Disable();
            await db.SaveChangesAsync();
        }

        await TransferHttp.PullAsync();

        TransferHttp.Feed.Requests.ShouldBeEmpty();
        (await TransferHttp.MovementsAsync(world.Safekeeping.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task EachTenantsAccount_IsReadOnItsOwn_AndItsMovementsBelongToIt()
    {
        var north = await TransferWorld.CreateAsync();
        var south = await TransferWorld.CreateAsync("South Advisory", "south-advisory");
        TransferHttp.Feed.AddMovement(north.Safekeeping.AccountNumber, CustodyMovementDirection.In, "ACME", 1m);
        TransferHttp.Feed.AddMovement(south.Safekeeping.AccountNumber, CustodyMovementDirection.In, "ACME", 2m);
        TransferHttp.Feed.Fail(north.Safekeeping.AccountNumber, CustodianFailure.Unavailable);

        var outcome = await TransferHttp.PullAsync();

        outcome.FailedSafekeepingAccounts.ShouldBe([north.Safekeeping.PublicId]);
        var southMovement = (await TransferHttp.MovementsAsync(south.Safekeeping.Id)).ShouldHaveSingleItem();
        (southMovement.TenantId, southMovement.InstrumentId).ShouldBe((south.Tenant.Id, (int?)await InstrumentIdAsync(south.InstrumentId)));
        (await TransferHttp.MovementsAsync(north.Safekeeping.Id)).ShouldBeEmpty();
    }

    private static async Task<int> InstrumentIdAsync(Guid publicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Instruments.IgnoreQueryFilters().Where(row => row.PublicId == publicId).Select(row => row.Id).SingleAsync();
    }
}
