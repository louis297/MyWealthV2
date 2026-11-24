namespace MyWealthV2.Simulator.Worlds;

/// <summary>Simulator spec R28: one row per control write that was not refused.</summary>
public sealed class ControlLogEntry
{
    private ControlLogEntry()
    {
    }

    public long Id { get; private set; }

    /// <summary>Real instant of the write.</summary>
    public DateTimeOffset At { get; private set; }

    /// <summary>Simulator now after the write.</summary>
    public DateTimeOffset SimulatorAt { get; private set; }

    /// <summary>The operator's email from the session.</summary>
    public string Actor { get; private set; } = "";

    public string Action { get; private set; } = "";

    /// <summary>The request body, as JSON.</summary>
    public string Detail { get; private set; } = "{}";

    public static ControlLogEntry Create(
        DateTimeOffset at,
        DateTimeOffset simulatorAt,
        string actor,
        string action,
        string detail) => new()
    {
        At = at.ToUniversalTime(),
        SimulatorAt = simulatorAt.ToUniversalTime(),
        Actor = actor,
        Action = action,
        Detail = detail
    };
}

public static class ControlActions
{
    public const string ClockAdvanced = "ClockAdvanced";
    public const string ClockPaused = "ClockPaused";
    public const string ClockResumed = "ClockResumed";
    public const string PartyOutageChanged = "PartyOutageChanged";
    public const string WorldReset = "WorldReset";
    public const string BankAccountOpened = "BankAccountOpened";
    public const string BankReceiptAdded = "BankReceiptAdded";
    public const string BankPaymentModeChanged = "BankPaymentModeChanged";
    public const string BankPaymentExecuted = "BankPaymentExecuted";
    public const string BankPaymentReturned = "BankPaymentReturned";
    public const string BankReceiptRecalled = "BankReceiptRecalled";
    public const string BankChargeAdded = "BankChargeAdded";
    public const string BankInterestPaid = "BankInterestPaid";
    public const string MarketInstrumentAdded = "MarketInstrumentAdded";
    public const string MarketPriceSet = "MarketPriceSet";
    public const string MarketDailyMoveSet = "MarketDailyMoveSet";
    public const string CorporateActionAnnounced = "CorporateActionAnnounced";
    public const string CorporateActionAmended = "CorporateActionAmended";
    public const string CorporateActionCancelled = "CorporateActionCancelled";
    public const string BrokerOrderModeChanged = "BrokerOrderModeChanged";
    public const string BrokerSlippageSet = "BrokerSlippageSet";
    public const string BrokerSettlementDaysSet = "BrokerSettlementDaysSet";
    public const string BrokerOrderFilled = "BrokerOrderFilled";
    public const string BrokerOrderCancelled = "BrokerOrderCancelled";

    /// <summary>Trade-settlement R38.</summary>
    public const string BrokerSettlementModeChanged = "BrokerSettlementModeChanged";

    /// <summary>Trade-settlement R38.</summary>
    public const string BrokerSettlementPaid = "BrokerSettlementPaid";

    /// <summary>Trade-settlement R40.</summary>
    public const string BrokerFillBroken = "BrokerFillBroken";

    public const string CustodianAccountOpened = "CustodianAccountOpened";

    public const string CustodianTransferModeChanged = "CustodianTransferModeChanged";

    public const string CustodianTransferExecuted = "CustodianTransferExecuted";

    public const string CustodianMovementAdded = "CustodianMovementAdded";

    public const string CustodianRoundingModeChanged = "CustodianRoundingModeChanged";
}
