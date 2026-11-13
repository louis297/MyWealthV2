namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>
/// An Adviser tried to change or disable a Shared portfolio (api-design §4.2 <c>portfolio_shared</c>;
/// portfolios amendment 2026-11-13 PB3, PB4). The detail names no account or Adviser outside the caller's Book.
/// </summary>
public sealed class PortfolioSharedException()
    : ProblemException(
        ProblemKind.Conflict,
        "portfolio_shared",
        "Portfolio is shared",
        "This portfolio is assigned to accounts outside your book. Ask a TenantAdmin to change or disable it.");
