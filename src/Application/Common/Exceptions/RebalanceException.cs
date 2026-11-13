namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>A rebalance failure with one of the portfolios §11 codes (api-design §4.2 registry).</summary>
public sealed class RebalanceException(string code, string message)
    : ProblemException(ProblemKind.Rule, code, "Rebalance failed", message);
