namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The transfer behind a <c>StockIn</c> or <c>StockOut</c> on a Custodial account (in-kind-transfers R38): exactly one
/// on a Transaction with source <c>InKindTransfer</c> and on its Reversal; immutable.
/// </summary>
public class TransactionInKindTransferDetail
{
    private TransactionInKindTransferDetail()
    {
    }

    public int TransactionId { get; private set; }

    public int TenantId { get; private set; }

    public Guid TransferPublicId { get; private set; }

    public string CounterpartyName { get; private set; } = string.Empty;

    /// <summary>The counterparty account masked to its last four letters or digits (<c>****1234</c>).</summary>
    public string CounterpartyAccountMasked { get; private set; } = string.Empty;

    public bool ChangesBeneficialOwner { get; private set; }

    public DateOnly? AcquiredOn { get; private set; }

    /// <summary>The custodian's settlement date of the movement that confirmed the transfer.</summary>
    public DateOnly SettlementDate { get; private set; }

    /// <summary>The movement's outside id.</summary>
    public string CustodianReference { get; private set; } = string.Empty;

    internal static TransactionInKindTransferDetail Create(int tenantId, Custody.InKindTransferOrigin origin) =>
        throw new NotImplementedException();

    /// <summary>A new row with the same values, for a Reversal.</summary>
    internal TransactionInKindTransferDetail Copy() =>
        new()
        {
            TenantId = TenantId,
            TransferPublicId = TransferPublicId,
            CounterpartyName = CounterpartyName,
            CounterpartyAccountMasked = CounterpartyAccountMasked,
            ChangesBeneficialOwner = ChangesBeneficialOwner,
            AcquiredOn = AcquiredOn,
            SettlementDate = SettlementDate,
            CustodianReference = CustodianReference
        };
}
