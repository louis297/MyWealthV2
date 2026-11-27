namespace MyWealthV2.Domain.Tax;

/// <summary>
/// One tax event of a Customer (tax-year-events R9–R16), in the Account's currency: nothing here is in NZD yet.
/// <see cref="Ordinal"/> is its place in the Customer's whole ledger, 0 for the first. <see cref="Quantity"/> is the
/// change of the tax holding's quantity (0 for a dividend and a move); <see cref="Changes"/> is the change on each
/// Account, which for a move is the quantity leaving one Account and arriving on the other. A move has the
/// <c>bookedAt</c>, the Transaction and the Account of its later leg.
/// </summary>
public sealed record TaxEvent(
    int Ordinal,
    TaxEventKind Kind,
    TransactionType LedgerType,
    int TransactionId,
    Guid TransactionPublicId,
    DateTimeOffset BookedAt,
    int InstrumentId,
    int AccountId,
    CustodyMode CustodyMode,
    decimal Quantity,
    IReadOnlyList<TaxQuantityChange> Changes,
    TaxTradeFigures? Trade = null,
    TaxDividendFigures? Dividend = null,
    TaxCarriedCost? CarriedCost = null,
    TaxMove? Move = null);

/// <summary>The change of one Account's quantity at an event.</summary>
public sealed record TaxQuantityChange(int AccountId, CustodyMode CustodyMode, decimal Quantity);

/// <summary>R10, R12: the principal and the fee in the Account's currency, each a positive figure.</summary>
public sealed record TaxTradeFigures(string Currency, decimal Principal, decimal Fee);

/// <summary>R11: the gross, the taxes and the imputation credit in the Account's currency, each a positive figure, with
/// the shares and the tax source of the dividend detail.</summary>
public sealed record TaxDividendFigures(
    string Currency,
    decimal Shares,
    DividendTaxSource TaxSource,
    decimal Gross,
    decimal ForeignTax,
    decimal Rwt,
    decimal Nrwt,
    decimal ImputationCredit);

/// <summary>R14, R16: the cost a security leg carries, in its currency, signed, and the stated acquisition date.</summary>
public sealed record TaxCarriedCost(decimal Amount, string Currency, DateOnly? AcquiredOn);

/// <summary>R15: the two legs of a move and the quantity that moved.</summary>
public sealed record TaxMove(decimal Quantity, TaxMoveLeg From, TaxMoveLeg To);

/// <summary>One leg of a move: its Account and its Transaction.</summary>
public sealed record TaxMoveLeg(
    int AccountId,
    CustodyMode CustodyMode,
    int TransactionId,
    Guid TransactionPublicId,
    DateTimeOffset BookedAt);
