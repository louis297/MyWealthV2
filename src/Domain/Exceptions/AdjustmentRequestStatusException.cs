namespace MyWealthV2.Domain.Exceptions;

/// <summary>The adjustment request is not <c>Requested</c>, or the approver is the requester (adjustment-review R7,
/// R8).</summary>
public class AdjustmentRequestStatusException(string message) : DomainException(message);
