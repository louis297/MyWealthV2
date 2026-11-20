namespace MyWealthV2.Domain.Exceptions;

/// <summary>The nominated bank account is not in a status its action starts from, or the reviewer is the person who
/// added it (withdrawals R8, R9).</summary>
public class NominatedBankAccountStatusException(string message) : DomainException(message);
