namespace MyWealthV2.Domain.Enums;

/// <summary>Why an execution report changed nothing (orders R25), in the order the reasons are tried.</summary>
public enum ExecutionReportSkipReason
{
    /// <summary>A kind the adapter did not know.</summary>
    UnsupportedKind = 0,

    /// <summary>The client order id is not a GUID, or names no order of the connection.</summary>
    UnknownOrder = 1,

    /// <summary>A <c>Settled</c> report names no fill of the order.</summary>
    UnknownFill = 2,

    /// <summary>A <c>Fill</c> of more than the order's quantity minus its filled quantity.</summary>
    Overfill = 3,

    /// <summary>A <c>Cancelled</c> or <c>Expired</c> report for an order that is not open, or a <c>Settled</c> report
    /// for a fill already settled.</summary>
    Repeated = 4
}
