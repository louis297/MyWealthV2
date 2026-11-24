using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Custodian;

/// <summary>
/// The custodian's election port (docs/features/dividends.md R21; ADR 0021 rules 12–14): tell the custodian of a
/// connection how many shares of one safekeeping account take part in a dividend's reinvestment plan. The instruction
/// id is the corporate action's PublicId, the key on every retry. A failure comes back as a result, never as an
/// exception.
/// </summary>
public interface ICustodianElections
{
    Task<CustodianElectionResult> SubmitAsync(CustodianElectionRequest request, CancellationToken cancellationToken);
}

/// <summary>R21: one election, built from the stored dividend only. No Tenant, Account or Customer.</summary>
public sealed record CustodianElectionRequest(
    string Connection,
    string AccountNumber,
    Guid InstructionId,
    string CorporateActionId,
    string Symbol,
    string Currency,
    decimal Quantity);

/// <summary>R21: accepted, rejected with a reason, or a failure of the call.</summary>
public sealed class CustodianElectionResult
{
    private CustodianElectionResult(bool isAccepted, ElectionRejectReason? rejectReason, CustodianFailure? failure)
    {
        IsAccepted = isAccepted;
        RejectReason = rejectReason;
        Failure = failure;
    }

    public bool IsAccepted { get; }

    public ElectionRejectReason? RejectReason { get; }

    public CustodianFailure? Failure { get; }

    public static CustodianElectionResult Accepted() => new(true, null, null);

    public static CustodianElectionResult Rejected(ElectionRejectReason reason) => new(false, reason, null);

    public static CustodianElectionResult Failed(CustodianFailure failure) => new(false, null, failure);
}
