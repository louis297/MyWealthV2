namespace MyWealthV2.Domain.Exceptions;

/// <summary>The trade settlement is not in a status its move starts from (trade-settlement R14, R15).</summary>
public class TradeSettlementStatusException(string message) : DomainException(message);
