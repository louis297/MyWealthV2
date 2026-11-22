namespace MyWealthV2.Domain.Enums;

/// <summary>What a bank statement line is (interest-run R39). The port's line carries it; a code the adapter does not
/// know is a transfer.</summary>
public enum StatementLineKind
{
    /// <summary>A payment in or out.</summary>
    Transfer = 0,

    /// <summary>Interest the bank pays or charges on the account itself.</summary>
    Interest = 1
}
