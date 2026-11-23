namespace MyWealthV2.Domain.Exceptions;

/// <summary>The rebalance is not in a status its move starts from (docs/features/rebalances.md R11, R13, R19).</summary>
public class RebalanceStatusException(string message) : DomainException(message);
