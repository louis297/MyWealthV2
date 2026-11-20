namespace MyWealthV2.Domain.ValueObjects;

/// <summary>
/// Finds the deposit references a payer may have typed in a bank reference text (deposit-matching R15).
/// </summary>
public static class DepositReferenceScanner
{
    /// <summary>Every distinct valid deposit reference in <paramref name="referenceText"/>, in the order found.</summary>
    public static IReadOnlyList<DepositReference> Candidates(string? referenceText) => throw new NotImplementedException();
}
