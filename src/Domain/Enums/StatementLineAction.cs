namespace MyWealthV2.Domain.Enums;

/// <summary>The last action a TenantAdmin took on a statement line (deposit-matching R31).</summary>
public enum StatementLineAction
{
    Matched = 0,
    Ignored = 1,
    ReturnRequested = 2,
    Reopened = 3,
    Unmatched = 4
}
