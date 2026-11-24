namespace MyWealthV2.Domain.Enums;

/// <summary>The reinvestment election of a dividend with the custodian (dividends R20–R22, R31).</summary>
public enum ReinvestmentElectionStatus
{
    /// <summary>No entitlement reinvests; the dividend is paid in cash.</summary>
    None = 0,

    /// <summary>To be sent to the custodian.</summary>
    Pending = 1,

    /// <summary>The custodian reinvests the elected quantity.</summary>
    Accepted = 2,

    /// <summary>The custodian refused; the dividend is paid in cash.</summary>
    Rejected = 3,

    /// <summary>Not used: too late to send, or released by a person; the dividend is paid in cash.</summary>
    Lapsed = 4
}
