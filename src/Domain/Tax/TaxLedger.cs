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
        var events = new List<TaxEvent>();
        foreach (var fact in facts
                     .Where(fact => !fact.Reversed && fact.Type != TransactionType.Reversal)
                     .OrderBy(fact => fact.BookedAt.UtcDateTime)
                     .ThenBy(fact => fact.TransactionId))
        {
            events.AddRange(EventsOf(fact));
        }

        return events.Select((taxEvent, ordinal) => taxEvent with { Ordinal = ordinal }).ToList();
    }

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
