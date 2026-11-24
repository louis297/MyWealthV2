namespace MyWealthV2.Domain.ValueObjects;

/// <summary>A signed quantity of one symbol, as the custodian or a movement names it.</summary>
public sealed record CustodianSymbolQuantity(string Symbol, decimal Quantity);

/// <summary>A signed quantity of one Instrument of the Tenant, with its symbol.</summary>
public sealed record CustodianInstrumentQuantity(int InstrumentId, string Symbol, decimal Quantity);

/// <summary>
/// What Application read at <c>SNAPSHOT</c> for one custodian reconciliation (docs/features/in-kind-transfers.md R50–R55):
/// the custodian side, the platform side per Instrument quoted in the account's currency, and the quantities that
/// explain a difference. Signs as in R53: a movement <c>In</c> and a buy are plus.
/// </summary>
public sealed record CustodianReconciliationInputs
{
    public required int TenantId { get; init; }

    public required int SafekeepingAccountId { get; init; }

    public required string Currency { get; init; }

    /// <summary>When the stored positions were written; null when the cursor never stored positions (R54).</summary>
    public DateTimeOffset? PositionsAt { get; init; }

    public required IReadOnlyList<CustodianSymbolQuantity> Positions { get; init; }

    /// <summary>R51: every Instrument of the Tenant quoted in the currency, with the sum of its legs on Custodial accounts.</summary>
    public required IReadOnlyList<CustodianInstrumentQuantity> Platform { get; init; }

    /// <summary>R53: the signed quantities of the <c>Received</c> movements of the account, per symbol.</summary>
    public required IReadOnlyList<CustodianSymbolQuantity> NotProcessed { get; init; }

    /// <summary>R53: the signed quantities of the <c>Unmatched</c> movements of the account, per symbol.</summary>
    public required IReadOnlyList<CustodianSymbolQuantity> Suspense { get; init; }

    /// <summary>R53: the signed quantities of the posted fills that are not broken and not delivered, per Instrument.</summary>
    public required IReadOnlyList<CustodianInstrumentQuantity> FillsNotDelivered { get; init; }

    /// <summary>R53: the signed quantities of the delivered fills that are still <c>Pending</c>, per Instrument.</summary>
    public required IReadOnlyList<CustodianInstrumentQuantity> DeliveredPendingFills { get; init; }

    /// <summary>R55: transfers on the account that are <c>Sent</c>, or <c>Approved</c> with a send attempt.</summary>
    public required int TransfersInFlight { get; init; }

    /// <summary>R55: the number of posted fills not delivered.</summary>
    public required int FillsNotDeliveredCount { get; init; }

    public DateTimeOffset? FeedLastSucceededAt { get; init; }

    public DateTimeOffset? FeedLastFailedAt { get; init; }

    public CustodianFailure? FeedLastFailure { get; init; }
}
