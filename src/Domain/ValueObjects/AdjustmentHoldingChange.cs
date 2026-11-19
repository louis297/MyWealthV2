namespace MyWealthV2.Domain.ValueObjects;

/// <summary>
/// The holding part of an adjustment (adjustments R10, R11): the signed changes as entered, and the holding's
/// projection before the adjustment (0 and 0 when there is none).
/// </summary>
public sealed record AdjustmentHoldingChange(
    int InstrumentId,
    string QuoteCurrency,
    decimal QuantityChange,
    decimal CostChange,
    decimal CurrentQuantity,
    decimal CurrentCost);
