namespace MyWealthV2.Application.Portfolios;

public sealed record RebalanceResultDto(string Status);

public sealed record AccountPortfolioDto(
    Guid AccountId,
    Guid PortfolioId,
    string Status,
    DateTimeOffset? LastRebalanceAt,
    string? LastRebalanceStatus,
    string? LastRebalanceMessage);
