namespace MyWealthV2.Domain.Enums;

/// <summary>
/// The kind of process that wrote a Transaction (transaction-source R2). Later flows add the next value in their own
/// spec and widen <c>CK_Transactions_SourceKind</c>.
/// </summary>
public enum TransactionSourceKind
{
    /// <summary>A person, through a create route or reverse (R4, R7).</summary>
    Manual = 0,

    /// <summary>The Development seeds: <c>TestSeed</c> and the Development smoke seed (R6).</summary>
    Seed = 1,

    /// <summary>A portfolio rebalance; the source id is the rebalance handle (R5).</summary>
    Rebalance = 2,

    /// <summary>The bank feed; the source id is the statement line's PublicId (deposit-matching R32).</summary>
    BankFeed = 3
}
