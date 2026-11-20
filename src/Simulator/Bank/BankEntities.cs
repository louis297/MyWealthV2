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

/// <summary>One line on a bank account (deposit-matching R41): a receipt, a payment out (withdrawals R53) or the return
/// of a payment (R54).</summary>
public sealed class BankLine
{
    public const int CreditDirection = 0;
    public const int DebitDirection = 1;

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

    /// <summary>Withdrawals R53: the instruction id of the payment that caused a debit line, echoed as sent.</summary>
    public string? EndToEndId { get; private set; }

    /// <summary>Withdrawals R54: the line a return takes back.</summary>
    public long? ReturnOfLineId { get; private set; }

    public string? ReturnReason { get; private set; }

    /// <summary>Withdrawals R53: an executed payment, in the feed at once. The payee is in the payer fields (R48).</summary>
    public static BankLine Payment(string outsideId, BankPayment payment, DateOnly valueDate, DateTimeOffset postedAt) => new()
    {
        OutsideId = outsideId,
        BankAccountId = payment.BankAccountId,
        Direction = DebitDirection,
        Amount = payment.Amount,
        ValueDate = valueDate,
        PayerName = payment.PayeeName,
        PayerAccount = payment.PayeeAccountNumber,
        Reference = payment.Reference,
        PostedAt = postedAt,
        AvailableAt = postedAt,
        EndToEndId = payment.InstructionId
    };

    /// <summary>Withdrawals R54: the payee's bank sends a payment back; the payee is in the payer fields.</summary>
    public static BankLine PaymentReturn(
        string outsideId,
        BankPayment payment,
        long debitLineId,
        string reason,
        DateOnly valueDate,
        DateTimeOffset postedAt,
        DateTimeOffset availableAt) => new()
    {
        OutsideId = outsideId,
        BankAccountId = payment.BankAccountId,
        Direction = CreditDirection,
        Amount = payment.Amount,
        ValueDate = valueDate,
        PayerName = payment.PayeeName,
        PayerAccount = payment.PayeeAccountNumber,
        Reference = payment.Reference,
        PostedAt = postedAt,
        AvailableAt = availableAt,
        ReturnOfLineId = debitLineId,
        ReturnReason = reason
    };

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

/// <summary>How the bank answers new payments (withdrawals R52).</summary>
public enum BankPaymentMode
{
    Execute = 0,
    Hold = 1,
    Reject = 2
}

/// <summary>Where a payment is in the simulator bank's books (withdrawals R55).</summary>
public enum BankPaymentStatus
{
    Held = 0,
    Executed = 1,
    Returned = 2,
    Rejected = 3
}

/// <summary>
/// One payment the platform asked the bank to make from a trust account (withdrawals R49–R55). The instruction id is
/// stored as an opaque string (R57); a second call with it and the same body gets the first answer (R51).
/// </summary>
public sealed class BankPayment
{
    public long Id { get; private set; }

    public string PaymentId { get; private set; } = "";

    public int BankAccountId { get; private set; }

    public string InstructionId { get; private set; } = "";

    public byte[] RequestHash { get; private set; } = [];

    public decimal Amount { get; private set; }

    public string? Reference { get; private set; }

    public string PayeeAccountNumber { get; private set; } = "";

    public string PayeeName { get; private set; } = "";

    public BankPaymentStatus Status { get; private set; }

    public string? RejectReason { get; private set; }

    /// <summary>The payment mode when the payment arrived.</summary>
    public BankPaymentMode Mode { get; private set; }

    /// <summary>Simulator time.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    public long? DebitLineId { get; private set; }

    public long? ReturnLineId { get; private set; }

    public static BankPayment Receive(
        string paymentId,
        int bankAccountId,
        string instructionId,
        byte[] requestHash,
        decimal amount,
        string? reference,
        string payeeAccountNumber,
        string payeeName,
        BankPaymentMode mode,
        string? rejectReason,
        DateTimeOffset simulatorNow) => new()
    {
        PaymentId = paymentId,
        BankAccountId = bankAccountId,
        InstructionId = instructionId,
        RequestHash = requestHash,
        Amount = amount,
        Reference = reference,
        PayeeAccountNumber = payeeAccountNumber,
        PayeeName = payeeName,
        Mode = mode,
        Status = mode switch
        {
            BankPaymentMode.Reject => BankPaymentStatus.Rejected,
            BankPaymentMode.Hold => BankPaymentStatus.Held,
            _ => BankPaymentStatus.Executed
        },
        RejectReason = mode == BankPaymentMode.Reject ? rejectReason : null,
        ReceivedAt = simulatorNow
    };

    /// <summary>R53: executed at once, or later from <c>Held</c>; the debit line is its record on the statement.</summary>
    public void Executed(long debitLineId)
    {
        Status = BankPaymentStatus.Executed;
        DebitLineId = debitLineId;
    }

    /// <summary>R54: the payee's bank sent it back.</summary>
    public void Returned(long returnLineId)
    {
        Status = BankPaymentStatus.Returned;
        ReturnLineId = returnLineId;
    }
}
