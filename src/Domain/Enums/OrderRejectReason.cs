namespace MyWealthV2.Domain.Enums;

/// <summary>Why the broker refused an order (orders R21). A reason the adapter does not know is <c>Refused</c>.</summary>
public enum OrderRejectReason
{
    UnknownInstrument = 0,
    InvalidOrder = 1,
    MarketClosed = 2,
    Refused = 3
}
