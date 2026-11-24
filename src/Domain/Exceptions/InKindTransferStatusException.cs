namespace MyWealthV2.Domain.Exceptions;

/// <summary>The in-kind transfer is not in a status its action starts from, or the approver is the requester
/// (in-kind-transfers R30–R33).</summary>
public class InKindTransferStatusException(string message) : DomainException(message);
