namespace MyWealthV2.Domain.ValueObjects;

/// <summary>
/// The process that wrote a Transaction: a kind and an optional source PublicId (transaction-source R2, R3). Set when
/// the Transaction is built and never changed.
/// </summary>
public sealed class TransactionSource : ValueObject
{
    private TransactionSource(TransactionSourceKind kind, Guid? publicId)
    {
        Kind = kind;
        PublicId = publicId;
    }

    public TransactionSourceKind Kind { get; }

    /// <summary>The PublicId of the instruction, event or run that wrote the Transaction; null for <c>Manual</c> and <c>Seed</c>.</summary>
    public Guid? PublicId { get; }

    public static TransactionSource Manual { get; } = new(TransactionSourceKind.Manual, null);

    public static TransactionSource Seed { get; } = new(TransactionSourceKind.Seed, null);

    public static TransactionSource Rebalance(Guid handle) => new(TransactionSourceKind.Rebalance, handle);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Kind;
        yield return PublicId ?? Guid.Empty;
    }
}
