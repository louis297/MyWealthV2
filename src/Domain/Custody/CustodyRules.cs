namespace MyWealthV2.Domain.Custody;

/// <summary>
/// Custody modes §4.2: the manual entry matrix, the one place it lives. It says which <see cref="TransactionType"/>
/// a caller may enter on an Account of each custody mode, through a create route or reverse. Application calls it;
/// the <see cref="Transaction"/> factories do not, so the platform's own writes are not limited by it (R7).
/// </summary>
public static class CustodyRules
{
    /// <summary>Deposit matching R35: a Custodial account's cash comes in only through the bank statement.</summary>
    public const string TransferInMessage =
        "Deposits to a Custodial account come from the bank statement. The customer pays in with the account's deposit instructions.";

    /// <summary>Withdrawals R45: money leaves a Custodial account only as a withdrawal.</summary>
    public const string TransferOutMessage =
        "Withdrawals from a Custodial account are paid to the customer's nominated bank account. Request a withdrawal.";

    /// <summary>
    /// Withdrawals R46: the <c>errors.Type</c> message for a hand <c>CloseOut</c> of <paramref name="cashSum"/>, or null
    /// when it is allowed: always on an External account, and on a Custodial account only for a remainder smaller than
    /// one minor unit of the currency (<c>0 &lt; SUM &lt; 10^−decimalPlaces</c>).
    /// </summary>
    public static string? CloseOutRefusal(CustodyMode custodyMode, decimal cashSum, string currency, int decimalPlaces)
    {
        if (custodyMode == CustodyMode.External)
        {
            return null;
        }

        var minorUnit = MinorUnit(decimalPlaces);
        return cashSum > 0 && cashSum < minorUnit
            ? null
            : $"Only a remainder smaller than {minorUnit.ToString(System.Globalization.CultureInfo.InvariantCulture)} {currency} can be closed out on a Custodial account. Withdraw the balance first.";
    }

    /// <summary>One minor unit of a currency with <paramref name="decimalPlaces"/>: 0.01 for two, 1 for none.</summary>
    private static decimal MinorUnit(int decimalPlaces)
    {
        var unit = 1m;
        for (var place = 0; place < decimalPlaces; place++)
        {
            unit /= 10m;
        }

        return unit;
    }

    /// <summary>Withdrawals R47 (roadmap D14): the <c>errors.ToAccountId</c> message for a transfer between two
    /// Customers' Custodial accounts.</summary>
    public const string CustodialTransferMessage = "Cash can be transferred between Custodial accounts of the same customer only.";

    /// <summary>Withdrawals R47: an <c>InternalTransfer</c> between Custodial accounts stays with one Customer; it would
    /// otherwise be a third-party payment (roadmap X4). External accounts are not limited.</summary>
    public static bool AllowsCustodialTransfer(CustodyMode custodyMode, int fromCustomerId, int toCustomerId) => true;

    private const string InterestMessage = "Interest cannot be entered by hand on a Custodial account.";
    private const string OpeningMessage = "A Custodial account starts empty. Fund it with TransferIn.";
    private const string StockMessage = "Securities cannot be moved in or out of a Custodial account by hand. Use Buy and Sell.";
    private const string CorporateActionMessage = "Corporate actions cannot be entered by hand on a Custodial account.";
    private const string DividendMessage = "Dividend is reserved and cannot be entered.";

    /// <summary>Adjustments R5: the <c>errors.AccountId</c> message for an adjustment on an External account.</summary>
    public const string AdjustmentMessage = "An adjustment can be entered only on a Custodial account.";

    /// <summary>Transaction source R13: the <c>errors</c> message when a hand reverse is refused by source.</summary>
    public const string HandReverseMessage = "This transaction was written by the platform and cannot be reversed by hand.";

    /// <summary>
    /// Transaction source R12, R13, R15: whether a person may reverse a Transaction of <paramref name="sourceKind"/>
    /// on an Account of <paramref name="custodyMode"/>. Application calls it in the reverse handler only;
    /// <see cref="Transaction.Reverse"/> does not, so a later flow can undo its own write (R16).
    /// </summary>
    public static bool AllowsHandReverse(CustodyMode custodyMode, TransactionSourceKind sourceKind) =>
        custodyMode == CustodyMode.External
        || sourceKind is TransactionSourceKind.Manual or TransactionSourceKind.Rebalance;

    public static bool AllowsManualEntry(CustodyMode custodyMode, TransactionType type) =>
        RefusalMessage(custodyMode, type) is null;

    /// <summary>The <c>errors.Type</c> message for a type the matrix refuses (§4.2.1), or null when it is allowed.</summary>
    public static string? RefusalMessage(CustodyMode custodyMode, TransactionType type)
    {
        if (type == TransactionType.Dividend)
        {
            return DividendMessage;
        }

        if (custodyMode == CustodyMode.External)
        {
            return type == TransactionType.Adjustment ? AdjustmentMessage : null;
        }

        return type switch
        {
            TransactionType.TransferIn => TransferInMessage,
            TransactionType.TransferOut => TransferOutMessage,
            TransactionType.Interest => InterestMessage,
            TransactionType.Opening or TransactionType.OpeningHoldings => OpeningMessage,
            TransactionType.StockIn or TransactionType.StockOut => StockMessage,
            TransactionType.Split or TransactionType.Bonus or TransactionType.Scrip => CorporateActionMessage,
            _ => null
        };
    }
}
