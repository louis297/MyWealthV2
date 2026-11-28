namespace MyWealthV2.Domain.Tax;

/// <summary>
/// Tax-year-events R6–R17: the tax events of a Customer, over every tax year, from the facts of their ledger. Reads no
/// clock and no store.
/// </summary>
public static class TaxLedger
{
    /// <summary>
    /// The events of <paramref name="facts"/> in the order of R8: by <c>bookedAt</c>, then by the internal Transaction
    /// id; the events of one Transaction in the order of R9. A reversed Transaction and every Reversal give none (R7).
    /// </summary>
    public static IReadOnlyList<TaxEvent> Build(IEnumerable<LedgerFact> facts)
    {
        var events = new List<(TaxEvent Event, LedgerFact Fact)>();
        foreach (var fact in facts
                     .Where(fact => !fact.Reversed && fact.Type != TransactionType.Reversal)
                     .OrderBy(fact => fact.BookedAt.UtcDateTime)
                     .ThenBy(fact => fact.TransactionId))
        {
            events.AddRange(EventsOf(fact).Select(taxEvent => (taxEvent, fact)));
        }

        return Paired(events).Select((taxEvent, ordinal) => taxEvent with { Ordinal = ordinal }).ToList();
    }

    /// <summary>
    /// Income-summary R3: one item per <c>Interest</c> of <paramref name="facts"/>, in the order of R8. A reversed
    /// Transaction and every Reversal give none. <see cref="Build"/> gives no event for an <c>Interest</c> (R17).
    /// </summary>
    public static IReadOnlyList<TaxInterest> BuildInterest(IEnumerable<LedgerFact> facts) => [];

    /// <summary>
    /// R22: each Account's quantity of each Instrument before <paramref name="instant"/>, from the events booked before
    /// it; a move counts from its later leg. A quantity of 0 is left out.
    /// </summary>
    public static IReadOnlyList<TaxQuantity> QuantitiesAt(IEnumerable<TaxEvent> events, DateTimeOffset instant) =>
        events
            .Where(taxEvent => taxEvent.BookedAt < instant)
            .SelectMany(taxEvent => taxEvent.Changes.Select(change => (change, taxEvent.InstrumentId)))
            .GroupBy(row => (row.change.AccountId, row.change.CustodyMode, row.InstrumentId))
            .Select(group => new TaxQuantity(group.Key.AccountId, group.Key.CustodyMode, group.Key.InstrumentId, group.Sum(row => row.change.Quantity)))
            .Where(quantity => quantity.Quantity != 0m)
            .OrderBy(quantity => quantity.AccountId)
            .ThenBy(quantity => quantity.InstrumentId)
            .ToList();

    /// <summary>The most a departure and an arrival of one move may be apart (R15).</summary>
    public static readonly TimeSpan MoveWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// R15: each departure, in the order of R8, takes the arrival nearest in time that is not yet paired, then the
    /// earlier by R8, when both are of the same Instrument and quantity, on two different Accounts, at most 30 days
    /// apart, and neither changes the beneficial owner. A pair becomes one move at the place of its later leg.
    /// </summary>
    private static IEnumerable<TaxEvent> Paired(IReadOnlyList<(TaxEvent Event, LedgerFact Fact)> events)
    {
        var partnerOf = new Dictionary<int, int>();
        for (var d = 0; d < events.Count; d++)
        {
            var departure = events[d];
            if (departure.Event.Kind != TaxEventKind.Departure || ChangesOwner(departure.Fact))
            {
                continue;
            }

            int? best = null;
            for (var a = 0; a < events.Count; a++)
            {
                var arrival = events[a];
                if (arrival.Event.Kind != TaxEventKind.Arrival
                    || partnerOf.ContainsKey(a)
                    || ChangesOwner(arrival.Fact)
                    || arrival.Event.InstrumentId != departure.Event.InstrumentId
                    || arrival.Event.AccountId == departure.Event.AccountId
                    || arrival.Event.Quantity != -departure.Event.Quantity
                    || Gap(arrival.Event, departure.Event) > MoveWindow)
                {
                    continue;
                }

                if (best is null || Gap(arrival.Event, departure.Event) < Gap(events[best.Value].Event, departure.Event))
                {
                    best = a;
                }
            }

            if (best is { } paired)
            {
                partnerOf[d] = paired;
                partnerOf[paired] = d;
            }
        }

        for (var i = 0; i < events.Count; i++)
        {
            if (!partnerOf.TryGetValue(i, out var partner))
            {
                yield return events[i].Event;
            }
            else if (i > partner)
            {
                var (departure, arrival) = events[i].Event.Kind == TaxEventKind.Departure
                    ? (events[i].Event, events[partner].Event)
                    : (events[partner].Event, events[i].Event);
                yield return Move(events[i].Event, departure, arrival);
            }
        }
    }

    private static TaxEvent Move(TaxEvent later, TaxEvent departure, TaxEvent arrival)
    {
        var quantity = arrival.Quantity;
        return later with
        {
            Kind = TaxEventKind.Move,
            Quantity = 0m,
            Changes =
            [
                new TaxQuantityChange(departure.AccountId, departure.CustodyMode, -quantity),
                new TaxQuantityChange(arrival.AccountId, arrival.CustodyMode, quantity)
            ],
            CarriedCost = null,
            Move = new TaxMove(quantity, Leg(departure), Leg(arrival))
        };
    }

    private static TaxMoveLeg Leg(TaxEvent taxEvent) =>
        new(taxEvent.AccountId, taxEvent.CustodyMode, taxEvent.TransactionId, taxEvent.TransactionPublicId, taxEvent.BookedAt);

    private static bool ChangesOwner(LedgerFact fact) => fact.InKindTransfer?.ChangesBeneficialOwner == true;

    private static TimeSpan Gap(TaxEvent one, TaxEvent other) => (one.BookedAt - other.BookedAt).Duration();

    /// <summary>R9–R17: the events of one Transaction, before they are numbered.</summary>
    private static IEnumerable<TaxEvent> EventsOf(LedgerFact fact)
    {
        var legs = fact.SecurityLegs.OrderBy(leg => leg.LegId).ToList();
        switch (fact.Type)
        {
            case TransactionType.Buy:
            case TransactionType.Sell:
                var kind = fact.Type == TransactionType.Buy ? TaxEventKind.Acquisition : TaxEventKind.Disposal;
                foreach (var leg in legs)
                {
                    yield return FromLeg(fact, leg, kind) with { Trade = Trade(fact, leg.AccountId, CashLegRole.Principal) };
                }

                break;

            case TransactionType.Dividend:
                if (Dividend(fact) is { } dividend)
                {
                    yield return dividend;
                }

                // R12: a reinvestment is an acquisition of the reinvested amount, after the dividend.
                foreach (var leg in legs)
                {
                    yield return FromLeg(fact, leg, TaxEventKind.Acquisition) with
                    {
                        Trade = Trade(fact, leg.AccountId, CashLegRole.Reinvestment)
                    };
                }

                break;

            case TransactionType.Split:
            case TransactionType.Bonus:
            case TransactionType.Scrip:
                foreach (var leg in legs)
                {
                    yield return FromLeg(fact, leg, TaxEventKind.Reorganisation);
                }

                break;

            case TransactionType.OpeningHoldings:
            case TransactionType.StockIn:
                foreach (var leg in legs)
                {
                    yield return FromLeg(fact, leg, TaxEventKind.Arrival) with
                    {
                        CarriedCost = new TaxCarriedCost(leg.CostAmount, Code(leg.CostCurrency), fact.InKindTransfer?.AcquiredOn)
                    };
                }

                break;

            case TransactionType.StockOut:
                foreach (var leg in legs)
                {
                    yield return FromLeg(fact, leg, TaxEventKind.Departure);
                }

                break;

            case TransactionType.Adjustment:
                // R16: the holding part only; the cash part gives nothing.
                foreach (var leg in legs)
                {
                    yield return FromLeg(fact, leg, TaxEventKind.Correction) with
                    {
                        CarriedCost = new TaxCarriedCost(leg.CostAmount, Code(leg.CostCurrency), null)
                    };
                }

                break;
        }
    }

    private static TaxEvent FromLeg(LedgerFact fact, LedgerSecurityLeg leg, TaxEventKind kind) =>
        new(
            0,
            kind,
            fact.Type,
            fact.TransactionId,
            fact.TransactionPublicId,
            fact.BookedAt,
            leg.InstrumentId,
            leg.AccountId,
            leg.CustodyMode,
            leg.Quantity,
            [new TaxQuantityChange(leg.AccountId, leg.CustodyMode, leg.Quantity)]);

    /// <summary>R10, R12: the principal from the legs of <paramref name="principalRole"/> and the fee from the
    /// <c>Fee</c> legs on the Account, each as a positive figure.</summary>
    private static TaxTradeFigures Trade(LedgerFact fact, int accountId, CashLegRole principalRole)
    {
        var legs = fact.CashLegs.Where(leg => leg.AccountId == accountId).ToList();
        var principal = Math.Abs(legs.Where(leg => leg.Role == principalRole).Sum(leg => leg.Amount));
        var fee = principalRole == CashLegRole.Principal
            ? Math.Abs(legs.Where(leg => leg.Role == CashLegRole.Fee).Sum(leg => leg.Amount))
            : 0m;
        return new TaxTradeFigures(CurrencyOf(legs), principal, fee);
    }

    /// <summary>R11: one event per <c>Dividend</c>, on the Account of its <c>Principal</c> leg.</summary>
    private static TaxEvent? Dividend(LedgerFact fact)
    {
        if (fact.Dividend is not { } detail
            || fact.CashLegs.FirstOrDefault(leg => leg.Role == CashLegRole.Principal) is not { } principal)
        {
            return null;
        }

        var legs = fact.CashLegs.Where(leg => leg.AccountId == principal.AccountId).ToList();
        var figures = new TaxDividendFigures(
            CurrencyOf(legs),
            detail.Shares,
            detail.TaxSource,
            Math.Abs(principal.Amount),
            Tax(legs, TaxKind.ForeignWithholding),
            Tax(legs, TaxKind.Rwt),
            Tax(legs, TaxKind.Nrwt),
            detail.ImputationCredit);
        return new TaxEvent(
            0,
            TaxEventKind.Dividend,
            fact.Type,
            fact.TransactionId,
            fact.TransactionPublicId,
            fact.BookedAt,
            detail.InstrumentId,
            principal.AccountId,
            principal.CustodyMode,
            0m,
            [],
            Dividend: figures);
    }

    private static decimal Tax(IEnumerable<LedgerCashLeg> legs, TaxKind kind) =>
        Math.Abs(legs.Where(leg => leg.Role == CashLegRole.Tax && leg.TaxKind == kind).Sum(leg => leg.Amount));

    private static string CurrencyOf(IReadOnlyList<LedgerCashLeg> legs) =>
        legs.Count == 0 ? string.Empty : Code(legs[0].Currency);

    private static string Code(string currency) => currency.Trim().ToUpperInvariant();
}

/// <summary>An Account's quantity of an Instrument at an instant (R22).</summary>
public sealed record TaxQuantity(int AccountId, CustodyMode CustodyMode, int InstrumentId, decimal Quantity);
