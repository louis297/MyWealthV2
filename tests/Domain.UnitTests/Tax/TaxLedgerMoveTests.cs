using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Tax-year-events R15: a departure and an arrival on two Accounts of the Customer are one move.</summary>
public class TaxLedgerMoveTests
{
    private const int A3 = 3;

    [Test]
    public void WorkedExample_GivesTheEventsOfBothYearsInOrder_WithT10AndT11AsOneMove()
    {
        var events = TaxLedger.Build(Facts);

        events.Select(e => (e.Kind, e.TransactionId, e.Quantity)).ShouldBe([
            (TaxEventKind.Acquisition, 1, 100m),
            (TaxEventKind.Acquisition, 2, 40m),
            (TaxEventKind.Disposal, 3, -30m),
            (TaxEventKind.Dividend, 4, 0m),
            (TaxEventKind.Reorganisation, 7, 40m),
            (TaxEventKind.Reorganisation, 8, 70m),
            (TaxEventKind.Dividend, 9, 0m),
            (TaxEventKind.Acquisition, 9, 0.8m),
            (TaxEventKind.Move, 11, 0m),
            (TaxEventKind.Acquisition, 12, 20m)
        ]);
        events.Select(e => e.Ordinal).ShouldBe(Enumerable.Range(0, 10));
    }

    [Test]
    public void TheMoveOfTheWorkedExample_NamesBothLegs_AndIsPlacedAtTheLaterOne()
    {
        var move = TaxLedger.Build(Facts).Where(e => e.Kind == TaxEventKind.Move).ShouldHaveSingleItem();

        (move.BookedAt, move.TransactionPublicId, move.AccountId, move.CustodyMode).ShouldBe((T11.BookedAt, IdOf(11), A1, CustodyMode.Custodial));
        (move.LedgerType, move.InstrumentId).ShouldBe((TransactionType.StockIn, Aaa));
        move.Move.ShouldBe(new TaxMove(
            80m,
            new TaxMoveLeg(A2, CustodyMode.External, 10, IdOf(10), T10.BookedAt),
            new TaxMoveLeg(A1, CustodyMode.Custodial, 11, IdOf(11), T11.BookedAt)));
        move.Changes.ShouldBe([new TaxQuantityChange(A2, CustodyMode.External, -80m), new TaxQuantityChange(A1, CustodyMode.Custodial, 80m)]);
        (move.Trade, move.CarriedCost).ShouldBe((null, null));
    }

    [Test]
    public void AnArrivalBookedBeforeTheDeparture_IsPairedAtTheDeparture()
    {
        var arrival = In(20, A1, 50m, Utc(2025, 8, 1, 0, 0));
        var departure = Out(21, A2, 50m, Utc(2025, 8, 6, 0, 0));

        var move = TaxLedger.Build([arrival, departure]).ShouldHaveSingleItem();

        (move.Kind, move.TransactionId, move.BookedAt, move.AccountId).ShouldBe((TaxEventKind.Move, 21, departure.BookedAt, A2));
        move.Move!.From.TransactionId.ShouldBe(21);
        move.Move.To.TransactionId.ShouldBe(20);
    }

    [Test]
    public void ThirtyDaysApart_IsAMove()
    {
        var at = Utc(2025, 8, 1, 0, 0);

        TaxLedger.Build([Out(20, A2, 50m, at), In(21, A1, 50m, at.AddDays(30))]).ShouldHaveSingleItem().Kind.ShouldBe(TaxEventKind.Move);
    }

    [Test]
    public void ThirtyDaysAndOneTickApart_IsADepartureAndAnArrival()
    {
        var at = Utc(2025, 8, 1, 0, 0);

        TaxLedger.Build([Out(20, A2, 50m, at), In(21, A1, 50m, at.AddDays(30).AddTicks(1))])
            .Select(e => e.Kind).ShouldBe([TaxEventKind.Departure, TaxEventKind.Arrival]);
    }

    [Test]
    public void AnotherQuantity_IsNotPaired() =>
        Kinds(Out(20, A2, 100m, Utc(2025, 8, 1, 0, 0)), In(21, A1, 200m, Utc(2025, 8, 3, 0, 0)))
            .ShouldBe([TaxEventKind.Departure, TaxEventKind.Arrival]);

    [Test]
    public void TheSameAccount_IsNotPaired() =>
        Kinds(Out(20, A2, 50m, Utc(2025, 8, 1, 0, 0)), In(21, A2, 50m, Utc(2025, 8, 3, 0, 0)))
            .ShouldBe([TaxEventKind.Departure, TaxEventKind.Arrival]);

    [Test]
    public void AnotherInstrument_IsNotPaired() =>
        Kinds(Out(20, A2, 50m, Utc(2025, 8, 1, 0, 0)), In(21, A1, 50m, Utc(2025, 8, 3, 0, 0), instrumentId: Aaa + 1))
            .ShouldBe([TaxEventKind.Departure, TaxEventKind.Arrival]);

    [Test]
    public void AChangeOfBeneficialOwnerOnTheArrival_IsNotPaired() =>
        Kinds(Out(20, A2, 50m, Utc(2025, 8, 1, 0, 0)), In(21, A1, 50m, Utc(2025, 8, 3, 0, 0), changesOwner: true))
            .ShouldBe([TaxEventKind.Departure, TaxEventKind.Arrival]);

    [Test]
    public void AChangeOfBeneficialOwnerOnTheDeparture_IsNotPaired()
    {
        var departure = Out(20, A1, 50m, Utc(2025, 8, 1, 0, 0)) with { InKindTransfer = new LedgerInKindTransfer(true, null) };

        Kinds(departure, In(21, A2, 50m, Utc(2025, 8, 3, 0, 0))).ShouldBe([TaxEventKind.Departure, TaxEventKind.Arrival]);
    }

    [Test]
    public void AnOpeningHoldingsLeg_CanBeTheArrivalOfAMove()
    {
        var opening = Fact(21, Utc(2025, 8, 3, 0, 0), TransactionType.OpeningHoldings, [], [Security(21, A1, 50m, 900m)]);

        Kinds(Out(20, A2, 50m, Utc(2025, 8, 1, 0, 0)), opening).ShouldBe([TaxEventKind.Move]);
    }

    [Test]
    public void OneDepartureAndTwoArrivals_PairTheArrivalNearestInTime()
    {
        var departure = Out(20, A2, 50m, Utc(2025, 8, 10, 0, 0));
        var far = In(21, A1, 50m, Utc(2025, 8, 2, 0, 0));
        var near = In(22, A3, 50m, Utc(2025, 8, 13, 0, 0));

        var events = TaxLedger.Build([departure, far, near]);

        events.Select(e => (e.Kind, e.TransactionId)).ShouldBe([(TaxEventKind.Arrival, 21), (TaxEventKind.Move, 22)]);
        events[1].Move!.From.TransactionId.ShouldBe(20);
    }

    [Test]
    public void TwoArrivalsAsNearInTime_PairTheEarlierByTheOrderingKey()
    {
        var departure = Out(20, A2, 50m, Utc(2025, 8, 10, 0, 0));
        var before = In(22, A1, 50m, Utc(2025, 8, 8, 0, 0));
        var after = In(21, A3, 50m, Utc(2025, 8, 12, 0, 0));

        var move = TaxLedger.Build([departure, before, after]).Where(e => e.Kind == TaxEventKind.Move).ShouldHaveSingleItem();

        move.Move!.To.TransactionId.ShouldBe(22);
    }

    [Test]
    public void TwoDeparturesAndOneArrival_TheFirstDepartureByTheOrderingKeyTakesIt()
    {
        var first = Out(20, A2, 50m, Utc(2025, 8, 1, 0, 0));
        var second = Out(21, A3, 50m, Utc(2025, 8, 11, 0, 0));
        var arrival = In(22, A1, 50m, Utc(2025, 8, 12, 0, 0));

        var events = TaxLedger.Build([second, arrival, first]);

        events.Select(e => (e.Kind, e.TransactionId)).ShouldBe([(TaxEventKind.Departure, 21), (TaxEventKind.Move, 22)]);
        events[1].Move!.From.TransactionId.ShouldBe(20);
    }

    [Test]
    public void AMoveAcrossAYearEnd_CountsBothLegsFromTheLaterInstant()
    {
        var departure = Out(20, A2, 50m, Utc(2025, 3, 25, 0, 0));
        var arrival = In(21, A1, 50m, Utc(2025, 4, 5, 0, 0));

        var move = TaxLedger.Build([departure, arrival]).ShouldHaveSingleItem();

        move.BookedAt.ShouldBe(arrival.BookedAt);
        TaxYear.Of(move.BookedAt).ShouldBe(2026);
        move.Changes.Sum(change => change.Quantity).ShouldBe(0m);
    }

    [Test]
    public void AReversedDeparture_IsNotPaired() =>
        Kinds(Out(20, A2, 50m, Utc(2025, 8, 1, 0, 0)) with { Reversed = true }, In(21, A1, 50m, Utc(2025, 8, 3, 0, 0)))
            .ShouldBe([TaxEventKind.Arrival]);

    private static IEnumerable<TaxEventKind> Kinds(params LedgerFact[] facts) => TaxLedger.Build(facts).Select(e => e.Kind);

    private static LedgerFact Out(int id, int accountId, decimal quantity, DateTimeOffset bookedAt) =>
        Fact(id, bookedAt, TransactionType.StockOut, [], [Security(id, accountId, -quantity, -quantity * 10m)]);

    private static LedgerFact In(
        int id, int accountId, decimal quantity, DateTimeOffset bookedAt, int instrumentId = Aaa, bool changesOwner = false) =>
        Fact(id, bookedAt, TransactionType.StockIn, [], [Security(id, accountId, quantity, quantity * 10m, instrumentId)],
            inKind: new LedgerInKindTransfer(changesOwner, null));
}
