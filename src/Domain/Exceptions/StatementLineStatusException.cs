namespace MyWealthV2.Domain.Exceptions;

/// <summary>The statement line is not in a status its action starts from, or it is a debit line
/// (deposit-matching R23, R24).</summary>
public class StatementLineStatusException(string message) : DomainException(message);
