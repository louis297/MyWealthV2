namespace MyWealthV2.Domain.Exceptions;

/// <summary>The withdrawal is not in a status its action starts from, or the approver is the requester
/// (withdrawals R27, R28).</summary>
public class WithdrawalStatusException(string message) : DomainException(message);
