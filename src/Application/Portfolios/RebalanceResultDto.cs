namespace MyWealthV2.Application.Portfolios;

public sealed record RebalanceResultDto(string Status);

public sealed record RebalanceAccountHandleDto(Guid AccountId, Guid Handle);

public sealed record RebalanceAcceptedDto(Guid JobId, IReadOnlyList<RebalanceAccountHandleDto> Accounts);

public sealed record AccountPortfolioDto(
    Guid AccountId,
    Guid PortfolioId,
    string Status,
    DateTimeOffset? LastRebalanceAt,
    string? LastRebalanceStatus,
    string? LastRebalanceMessage);
