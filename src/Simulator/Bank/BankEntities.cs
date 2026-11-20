namespace MyWealthV2.Simulator.Bank;

/// <summary>A trust account the simulator bank holds (deposit-matching R38). Its own books: it never reads the
/// platform's client money accounts.</summary>
public sealed class BankAccount
{
    public int Id { get; private set; }

    public string AccountNumber { get; private set; } = "";

    public string Currency { get; private set; } = "";

    public string Name { get; private set; } = "";

    /// <summary>Simulator time.</summary>
    public DateTimeOffset OpenedAt { get; private set; }

    public static BankAccount Open(string accountNumber, string currency, string name, DateTimeOffset simulatorNow) => new()
    {
        AccountNumber = accountNumber,
        Currency = currency,
        Name = name,
        OpenedAt = simulatorNow
    };
}

/// <summary>One line on a bank account (deposit-matching R41). Every line in this slice is a credit.</summary>
public sealed class BankLine
{
    public const int CreditDirection = 0;

    public long Id { get; private set; }

    public string OutsideId { get; private set; } = "";

    public int BankAccountId { get; private set; }

    public int Direction { get; private set; }

    public decimal Amount { get; private set; }

    public DateOnly ValueDate { get; private set; }

    public string? PayerName { get; private set; }

    public string? PayerAccount { get; private set; }

    public string? Reference { get; private set; }

    /// <summary>Simulator time the receipt was added.</summary>
    public DateTimeOffset PostedAt { get; private set; }

    /// <summary>Simulator time from which the line takes a place in the feed (R43).</summary>
    public DateTimeOffset AvailableAt { get; private set; }

    public bool DeliverTwice { get; private set; }

    public static BankLine Receipt(
        string outsideId,
        int bankAccountId,
        decimal amount,
        DateOnly valueDate,
        string? payerName,
        string? payerAccount,
        string? reference,
        DateTimeOffset postedAt,
        DateTimeOffset availableAt,
        bool deliverTwice) => new()
    {
        OutsideId = outsideId,
        BankAccountId = bankAccountId,
        Direction = CreditDirection,
        Amount = amount,
        ValueDate = valueDate,
        PayerName = payerName,
        PayerAccount = payerAccount,
        Reference = reference,
        PostedAt = postedAt,
        AvailableAt = availableAt,
        DeliverTwice = deliverTwice
    };
}

/// <summary>A place in an account's statement feed (R43). A line with <c>deliverTwice</c> has two.</summary>
public sealed class BankFeedPlace
{
    public long Place { get; private set; }

    public int BankAccountId { get; private set; }

    public long BankLineId { get; private set; }

    public static BankFeedPlace For(BankLine line) => new() { BankAccountId = line.BankAccountId, BankLineId = line.Id };
}
