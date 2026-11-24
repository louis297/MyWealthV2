using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Custodian;

/// <summary>
/// The custodian's transfer port (docs/features/in-kind-transfers.md R74; ADR 0021 rules 12–14): send one in-kind
/// transfer instruction to the custodian of a connection, for one safekeeping account. The instruction id is the
/// idempotency key the custodian sees on every attempt. A failure comes back as a result, never as an exception.
/// </summary>
public interface ICustodianTransfers
{
    Task<CustodianTransferResult> SendAsync(CustodianTransferRequest request, CancellationToken cancellationToken);
}

/// <summary>The other broker, custodian or registry, as the instruction names it.</summary>
public sealed record CustodianCounterparty(string Name, string Account, string HolderName);

/// <summary>R34: one instruction, built from the stored transfer only. No Tenant, Account, Customer, cost or
/// ownership flag.</summary>
public sealed record CustodianTransferRequest(
    string Connection,
    string AccountNumber,
    Guid InstructionId,
    TransferDirection Direction,
    string Symbol,
    string Currency,
    decimal Quantity,
    CustodianCounterparty Counterparty);

/// <summary>R35: accepted with the custodian's transfer id, rejected with a reason, or a failure of the call.</summary>
public sealed class CustodianTransferResult
{
    private CustodianTransferResult(string? transferId, TransferRejectReason? rejectReason, CustodianFailure? failure)
    {
        TransferId = transferId;
        RejectReason = rejectReason;
        Failure = failure;
    }

    public string? TransferId { get; }

    public TransferRejectReason? RejectReason { get; }

    public CustodianFailure? Failure { get; }

    public static CustodianTransferResult Accepted(string transferId) => new(transferId, null, null);

    public static CustodianTransferResult Rejected(TransferRejectReason reason) => new(null, reason, null);

    public static CustodianTransferResult Failed(CustodianFailure failure) => new(null, null, failure);
}
