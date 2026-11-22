namespace MyWealthV2.Domain.Enums;

/// <summary>Where an order is (orders R14). Open while <c>Placed</c>, <c>Working</c> or <c>PartlyFilled</c>.</summary>
public enum OrderStatus
{
    /// <summary>The platform holds it; it waits to be sent, or the outcome of the send is not known yet.</summary>
    Placed = 0,

    /// <summary>The broker accepted it; no fill yet.</summary>
    Working = 1,

    /// <summary>Some of it is filled; the rest is still at the broker.</summary>
    PartlyFilled = 2,

    /// <summary>The whole quantity is filled.</summary>
    Filled = 3,

    /// <summary>Ended before it was filled in full: by a person before it was sent, or by the broker.</summary>
    Cancelled = 4,

    /// <summary>The broker refused it.</summary>
    Rejected = 5,

    /// <summary>The broker ended it at the end of its trading day.</summary>
    Expired = 6
}
