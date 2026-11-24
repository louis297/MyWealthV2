namespace MyWealthV2.Domain.Exceptions;

/// <summary>The custody movement is not in a status its action starts from (in-kind-transfers R18).</summary>
public class CustodyMovementStatusException(string message) : DomainException(message);
