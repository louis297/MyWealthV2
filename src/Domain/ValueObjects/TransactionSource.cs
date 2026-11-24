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

    /// <summary>A rebalance trade's source: the rebalance handle, shared by every trade of one run (R5).</summary>
    public static TransactionSource Rebalance(Guid handle)
    {
        if (handle == Guid.Empty)
        {
            throw new DomainException("A rebalance source needs the rebalance handle.");
        }

        return new TransactionSource(TransactionSourceKind.Rebalance, handle);
    }

    /// <summary>A deposit's source: the PublicId of the statement line it came from (deposit-matching R32).</summary>
    public static TransactionSource BankFeed(Guid statementLinePublicId)
    {
        if (statementLinePublicId == Guid.Empty)
        {
            throw new DomainException("A bank feed source needs the statement line's PublicId.");
        }

        return new TransactionSource(TransactionSourceKind.BankFeed, statementLinePublicId);
    }

    /// <summary>A payout's source: the PublicId of the withdrawal it pays (withdrawals R41).</summary>
    public static TransactionSource Payment(Guid withdrawalPublicId)
    {
        if (withdrawalPublicId == Guid.Empty)
        {
            throw new DomainException("A payment source needs the withdrawal's PublicId.");
        }

        return new TransactionSource(TransactionSourceKind.Payment, withdrawalPublicId);
    }

    /// <summary>A remainder write-off's source: the PublicId of the account closure (account-closure R24).</summary>
    public static TransactionSource AccountClosure(Guid closurePublicId)
    {
        if (closurePublicId == Guid.Empty)
        {
            throw new DomainException("An account closure source needs the closure's PublicId.");
        }

        return new TransactionSource(TransactionSourceKind.AccountClosure, closurePublicId);
    }

    /// <summary>An interest posting's source: the PublicId of the interest run (interest-run R23).</summary>
    public static TransactionSource InterestRun(Guid runPublicId)
    {
        if (runPublicId == Guid.Empty)
        {
            throw new DomainException("An interest run source needs the run's PublicId.");
        }

        return new TransactionSource(TransactionSourceKind.InterestRun, runPublicId);
    }

    /// <summary>An entitlement posting's source: the PublicId of the corporate action (corporate-actions R26).</summary>
    public static TransactionSource CorporateAction(Guid corporateActionPublicId)
    {
        if (corporateActionPublicId == Guid.Empty)
        {
            throw new DomainException("A corporate action source needs the corporate action's PublicId.");
        }

        return new TransactionSource(TransactionSourceKind.CorporateAction, corporateActionPublicId);
    }

    /// <summary>A fill's source: the PublicId of its order, shared by every fill of the order (orders R32).</summary>
    public static TransactionSource OrderFill(Guid orderPublicId)
    {
        if (orderPublicId == Guid.Empty)
        {
            throw new DomainException("An order fill source needs the order's PublicId.");
        }

        return new TransactionSource(TransactionSourceKind.OrderFill, orderPublicId);
    }

    /// <summary>A migrated opening balance's source: the PublicId of its migration batch, shared by every Transaction
    /// of the batch (migration-batches R27).</summary>
    public static TransactionSource Migration(Guid batchPublicId)
    {
        if (batchPublicId == Guid.Empty)
        {
            throw new DomainException("A migration source needs the migration batch's PublicId.");
        }

        return new TransactionSource(TransactionSourceKind.Migration, batchPublicId);
    }

    /// <summary>An in-kind transfer's posting: the PublicId of the transfer (in-kind-transfers R37, R49).</summary>
    public static TransactionSource InKindTransfer(Guid transferPublicId) => throw new NotImplementedException();

    /// <summary>The source as stored on a Transaction row.</summary>
    internal static TransactionSource FromStored(TransactionSourceKind kind, Guid? publicId) =>
        kind switch
        {
            TransactionSourceKind.Manual => Manual,
            TransactionSourceKind.Seed => Seed,
            TransactionSourceKind.Rebalance => Rebalance(publicId ?? Guid.Empty),
            TransactionSourceKind.BankFeed => BankFeed(publicId ?? Guid.Empty),
            TransactionSourceKind.Payment => Payment(publicId ?? Guid.Empty),
            TransactionSourceKind.AccountClosure => AccountClosure(publicId ?? Guid.Empty),
            TransactionSourceKind.InterestRun => InterestRun(publicId ?? Guid.Empty),
            TransactionSourceKind.CorporateAction => CorporateAction(publicId ?? Guid.Empty),
            TransactionSourceKind.OrderFill => OrderFill(publicId ?? Guid.Empty),
            TransactionSourceKind.Migration => Migration(publicId ?? Guid.Empty),
            TransactionSourceKind.InKindTransfer => new TransactionSource(kind, publicId),
            _ => new TransactionSource(kind, publicId)
        };

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Kind;
        yield return PublicId ?? Guid.Empty;
    }
}
