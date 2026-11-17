namespace MyWealthV2.Domain.Custody;

/// <summary>
/// Custody modes §4.2: the manual entry matrix, the one place it lives. It says which <see cref="TransactionType"/>
/// a caller may enter on an Account of each custody mode, through a create route or reverse. Application calls it;
/// the <see cref="Transaction"/> factories do not, so the platform's own writes are not limited by it (R7).
/// </summary>
public static class CustodyRules
{
    public static bool AllowsManualEntry(CustodyMode custodyMode, TransactionType type) => default;

    /// <summary>The <c>errors.Type</c> message for a type the matrix refuses (§4.2.1), or null when it is allowed.</summary>
    public static string? RefusalMessage(CustodyMode custodyMode, TransactionType type) => default;
}
