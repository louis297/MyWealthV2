namespace MyWealthV2.Domain.Exceptions;

/// <summary>The order is not in a status its move starts from, or already has a cancel request (orders R18).</summary>
public class OrderStatusException(string message) : DomainException(message);
