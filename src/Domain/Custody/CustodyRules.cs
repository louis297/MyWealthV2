namespace MyWealthV2.Domain.Custody;

/// <summary>
/// Custody modes §4.2: the manual entry matrix, the one place it lives. It says which <see cref="TransactionType"/>
/// a caller may enter on an Account of each custody mode, through a create route or reverse. Application calls it;
/// the <see cref="Transaction"/> factories do not, so the platform's own writes are not limited by it (R7).
/// </summary>
public static class CustodyRules
{
    private const string InterestMessage = "Interest cannot be entered by hand on a Custodial account.";
    private const string OpeningMessage = "A Custodial account starts empty. Fund it with TransferIn.";
    private const string StockMessage = "Securities cannot be moved in or out of a Custodial account by hand. Use Buy and Sell.";
    private const string CorporateActionMessage = "Corporate actions cannot be entered by hand on a Custodial account.";
    private const string DividendMessage = "Dividend is reserved and cannot be entered.";

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
            return null;
        }

        return type switch
        {
            TransactionType.Interest => InterestMessage,
            TransactionType.Opening or TransactionType.OpeningHoldings => OpeningMessage,
            TransactionType.StockIn or TransactionType.StockOut => StockMessage,
            TransactionType.Split or TransactionType.Bonus or TransactionType.Scrip => CorporateActionMessage,
            _ => null
        };
    }
}
