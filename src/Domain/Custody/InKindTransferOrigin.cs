namespace MyWealthV2.Domain.Custody;

/// <summary>
/// In-kind-transfers R38: what the Transaction of a completed transfer records about it. The counterparty account is
/// given in full and masked when the detail is built.
/// </summary>
public sealed record InKindTransferOrigin(
    Guid TransferPublicId,
    string CounterpartyName,
    string CounterpartyAccount,
    bool ChangesBeneficialOwner,
    DateOnly? AcquiredOn,
    DateOnly SettlementDate,
    string CustodianReference);
