namespace MyWealthV2.Application.Portfolios;

public sealed record RebalanceAccountHandleDto(Guid AccountId, Guid Handle);

public sealed record RebalanceAcceptedDto(Guid JobId, IReadOnlyList<RebalanceAccountHandleDto> Accounts);

/// <summary>Rebalances R25: the assignment item, with its newest rebalance.</summary>
public sealed record AccountPortfolioDto(
    Guid AccountId,
    Guid PortfolioId,
    string Status,
    AccountRebalanceDto? Rebalance);

/// <summary>Rebalances R25: <see cref="Failure"/> is set on a <c>Failed</c> or <c>Incomplete</c> rebalance.</summary>
public sealed record AccountRebalanceDto(
    Guid Id,
    string Status,
    Rebalances.RebalanceFailureDto? Failure,
    DateTimeOffset ProposedAt,
    DateTimeOffset? EndedAt);
