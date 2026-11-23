using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Rebalances;

/// <summary>Rebalances R10: the answer of a route that proposes, once the rebalance is committed.</summary>
internal static class RebalanceProposals
{
    /// <summary><c>{ status, rebalanceId }</c> for <c>Proposed</c> and <c>NoChange</c>; a <c>Failed</c> rebalance,
    /// already stored, is answered with its problem code.</summary>
    public static RebalanceProposalDto Answer(Rebalance rebalance) =>
        rebalance.Status == RebalanceStatus.Failed
            ? throw new RebalanceException(rebalance.FailureCode!, rebalance.FailureMessage!)
            : new RebalanceProposalDto(rebalance.Status.ToString(), rebalance.PublicId);
}
