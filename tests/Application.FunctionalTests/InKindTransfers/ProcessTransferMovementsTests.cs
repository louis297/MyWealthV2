using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>In-kind transfers R14, R15 rows 1–2, R36–R42: the custodian's movement completes its transfer.</summary>
[Parallelizable]
public class ProcessTransferMovementsTests : TestBase
{
    [Test]
    public async Task AMovementIn_NamingASentTransfer_PostsAStockIn_WithTheCost_TheDetail_AndCompletesTheTransfer()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.SentAsync();
        var movementId = world.AddConfirmation(id);

        await TransferHttp.PullAndProcessAsync();

        var transfer = await TransferHttp.GetAsync(world.TenantAdmin, id);
        transfer.GetProperty("status").GetString().ShouldBe("Completed");
        transfer.GetProperty("completedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        var transactionId = transfer.GetProperty("transactionId").GetGuid();
        var posting = await TransferHttp.TransactionAsync(world.Adviser, transactionId);
        posting.GetProperty("type").GetString().ShouldBe("StockIn");
        posting.GetProperty("memo").GetString().ShouldBe("Transfer in");
        posting.GetProperty("reference").ValueKind.ShouldBe(JsonValueKind.Null);
        posting.GetProperty("source").GetProperty("kind").GetString().ShouldBe("InKindTransfer");
        posting.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(id);
        posting.GetProperty("cash").GetArrayLength().ShouldBe(0);
        var leg = posting.GetProperty("security").EnumerateArray().Single();
        leg.GetProperty("quantity").GetDecimal().ShouldBe(100m);
        leg.GetProperty("cost").GetDecimal().ShouldBe(9500m);
        var detail = posting.GetProperty("inKindTransfer");
        detail.GetProperty("transferId").GetGuid().ShouldBe(id);
        detail.GetProperty("direction").GetString().ShouldBe("In");
        detail.GetProperty("counterpartyName").GetString().ShouldBe("Harbour Brokers");
        detail.GetProperty("counterpartyAccount").GetString().ShouldBe("****4172");
        detail.GetProperty("changesBeneficialOwner").GetBoolean().ShouldBeFalse();
        detail.GetProperty("acquiredOn").GetString().ShouldBe("2024-03-18");
        detail.GetProperty("settlementDate").GetString().ShouldBe(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"));
        detail.GetProperty("custodianReference").GetString().ShouldBe(movementId);
        var holding = (await TransferHttp.HoldingAsync(world.TenantAdmin, world.AccountId, world.InstrumentId))!.Value;
        holding.GetProperty("quantity").GetDecimal().ShouldBe(200m);
        holding.GetProperty("cost").GetDecimal().ShouldBe(12000m);
        var movement = await TransferHttp.MovementAsync(movementId);
        (movement.Status, movement.MatchMethod).ShouldBe((CustodyMovementStatus.Matched, (CustodyMatchMethod?)CustodyMatchMethod.Transfer));
        movement.TransactionId.ShouldNotBeNull();
        movement.InKindTransferId.ShouldBe((await TransferHttp.TransferAsync(id)).Id);
        (await TransferHttp.TransactionAsync(world.CustomerToken, transactionId)).GetProperty("inKindTransfer")
            .GetProperty("counterpartyAccount").GetString().ShouldBe("****4172");
    }

    [Test]
    public async Task AMovementOut_PostsAStockOut_AtAverageCost_AndReleasesTheReservation()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.SentAsync("Out", 40m);
        world.AddConfirmation(id, "Out", 40m);

        await TransferHttp.PullAndProcessAsync();

        var transfer = await TransferHttp.GetAsync(world.TenantAdmin, id);
        transfer.GetProperty("status").GetString().ShouldBe("Completed");
        var posting = await TransferHttp.TransactionAsync(world.TenantAdmin, transfer.GetProperty("transactionId").GetGuid());
        posting.GetProperty("type").GetString().ShouldBe("StockOut");
        posting.GetProperty("memo").GetString().ShouldBe("Transfer out");
        var leg = posting.GetProperty("security").EnumerateArray().Single();
        (leg.GetProperty("quantity").GetDecimal(), leg.GetProperty("cost").GetDecimal()).ShouldBe((-40m, -1000m));
        posting.GetProperty("inKindTransfer").GetProperty("acquiredOn").ValueKind.ShouldBe(JsonValueKind.Null);
        var holding = (await TransferHttp.HoldingAsync(world.TenantAdmin, world.AccountId, world.InstrumentId))!.Value;
        holding.GetProperty("quantity").GetDecimal().ShouldBe(60m);
        holding.GetProperty("reservedQuantity").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task AMovementThatArrivesWhileTheTransferIsApprovedWithAnAttempt_CompletesIt()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.ApprovedAsync();
        TransferHttp.Custodian.Answer(MyWealthV2.Application.Custodian.CustodianTransferResult.Failed(CustodianFailure.Timeout));
        await TransferHttp.SendAsync();
        world.AddConfirmation(id);

        await TransferHttp.PullAndProcessAsync();

        (await TransferHttp.TransferAsync(id)).Status.ShouldBe(InKindTransferStatus.Completed);
    }

    [TestCase("quantity")]
    [TestCase("direction")]
    [TestCase("instrument")]
    public async Task AMovementThatDiffersFromItsTransfer_IsUnmatchedTransferMismatch_AndPostsNothing(string differs)
    {
        var world = await TransferWorld.CreateAsync();
        await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "BETA");
        var id = await world.SentAsync();
        var transactionsBefore = await TransferHttp.TransactionCountAsync(world.AccountId);
        var movementId = differs switch
        {
            "quantity" => world.AddConfirmation(id, quantity: 99m),
            "direction" => world.AddConfirmation(id, direction: "Out"),
            _ => world.AddConfirmation(id, symbol: "BETA")
        };

        await TransferHttp.PullAndProcessAsync();

        var movement = await TransferHttp.MovementAsync(movementId);
        (movement.Status, movement.UnmatchedReason)
            .ShouldBe((CustodyMovementStatus.Unmatched, (CustodyUnmatchedReason?)CustodyUnmatchedReason.TransferMismatch));
        (await TransferHttp.TransferAsync(id)).Status.ShouldBe(InKindTransferStatus.Sent);
        (await TransferHttp.TransactionCountAsync(world.AccountId)).ShouldBe(transactionsBefore);
    }

    [Test]
    public async Task AMovementForATransferThatIsNotOpenOrNotSent_IsUnmatchedTransferNotOpen()
    {
        var world = await TransferWorld.CreateAsync();
        var cancelled = await world.RequestAsync();
        (await TransferHttp.ActAsync(world.Adviser, cancelled, "cancel")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var requested = await world.RequestAsync();
        var setAside = await world.SentAsync();
        (await TransferHttp.ActAsync(world.TenantAdmin, setAside, "set-aside", "Never executed")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var movements = new[] { world.AddConfirmation(cancelled), world.AddConfirmation(requested), world.AddConfirmation(setAside) };

        await TransferHttp.PullAndProcessAsync();

        foreach (var movementId in movements)
        {
            (await TransferHttp.MovementAsync(movementId)).UnmatchedReason.ShouldBe(CustodyUnmatchedReason.TransferNotOpen);
        }

        (await TransferHttp.TransferAsync(requested)).Status.ShouldBe(InKindTransferStatus.Requested);
        (await TransferHttp.TransferAsync(setAside)).Status.ShouldBe(InKindTransferStatus.SetAside);
    }

    [Test]
    public async Task AnOutForMoreThanTheHoldingAfterAConsolidation_IsUnmatchedInsufficientHolding()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.SentAsync("Out", 40m);
        await OrderHttp.ChangeHoldingAsync(world.AccountId, world.InstrumentId, -70m, -1750m);
        var movementId = world.AddConfirmation(id, "Out", 40m);

        await TransferHttp.PullAndProcessAsync();

        (await TransferHttp.MovementAsync(movementId)).UnmatchedReason.ShouldBe(CustodyUnmatchedReason.InsufficientHolding);
        (await TransferHttp.TransferAsync(id)).Status.ShouldBe(InKindTransferStatus.Sent);
        (await TransferHttp.HoldingAsync(world.TenantAdmin, world.AccountId, world.InstrumentId))!.Value
            .GetProperty("quantity").GetDecimal().ShouldBe(30m);
    }

    [Test]
    public async Task AMovementForAClosedAccount_IsUnmatchedAccountClosed()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.SentAsync();
        await OrderHttp.SetAccountStatusAsync(world.AccountId, AccountStatus.Closed);
        var movementId = world.AddConfirmation(id);

        await TransferHttp.PullAndProcessAsync();

        (await TransferHttp.MovementAsync(movementId)).UnmatchedReason.ShouldBe(CustodyUnmatchedReason.AccountClosed);
    }

    [Test]
    public async Task AnInstructionIdThatNamesNoTransferOfTheTenant_IsUnmatchedUnknownInstruction()
    {
        var north = await TransferWorld.CreateAsync();
        var south = await TransferWorld.CreateAsync("South Advisory", "south-advisory");
        var southTransfer = await south.SentAsync();
        var number = north.Safekeeping.AccountNumber;
        var ids = new[]
        {
            TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "ACME", 1m, instructionId: "not-a-guid").Id!,
            TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "ACME", 1m, instructionId: Guid.NewGuid().ToString()).Id!,
            TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "ACME", 100m, instructionId: southTransfer.ToString()).Id!
        };

        await TransferHttp.PullAndProcessAsync();

        foreach (var movementId in ids)
        {
            (await TransferHttp.MovementAsync(movementId)).UnmatchedReason.ShouldBe(CustodyUnmatchedReason.UnknownInstruction);
        }

        (await TransferHttp.TransferAsync(southTransfer)).Status.ShouldBe(InKindTransferStatus.Sent);
    }

    [Test]
    public async Task ADisabledCustomersTransfer_IsStillCompleted()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.SentAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);
        world.AddConfirmation(id);

        await TransferHttp.PullAndProcessAsync();

        (await TransferHttp.TransferAsync(id)).Status.ShouldBe(InKindTransferStatus.Completed);
    }

    [Test]
    public async Task TheTransfersTransaction_CannotBeReversedByHand()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.SentAsync();
        world.AddConfirmation(id);
        await TransferHttp.PullAndProcessAsync();
        var transactionId = (await TransferHttp.GetAsync(world.TenantAdmin, id)).GetProperty("transactionId").GetGuid();

        var response = await TransactionHttp.Send(HttpMethod.Post, $"/transactions/{transactionId}/reverse", world.TenantAdmin,
            idempotencyKey: Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ProcessingAgain_ChangesNothing()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.SentAsync();
        world.AddConfirmation(id);
        await TransferHttp.PullAndProcessAsync();
        var count = await TransferHttp.TransactionCountAsync(world.AccountId);

        await TransferHttp.PullAndProcessAsync();

        (await TransferHttp.TransactionCountAsync(world.AccountId)).ShouldBe(count);
    }

    [Test]
    public async Task OtherTransactions_HaveNoInKindTransfer()
    {
        var world = await TransferWorld.CreateAsync();
        var response = await TransferHttp.Send(HttpMethod.Get, $"/transactions?accountId={world.AccountId}", world.TenantAdmin);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        json.RootElement.GetProperty("items").EnumerateArray()
            .ShouldAllBe(item => item.GetProperty("inKindTransfer").ValueKind == JsonValueKind.Null);
    }
}
