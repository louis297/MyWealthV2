namespace MyWealthV2.Domain.Tax;

/// <summary>
/// What the tax year walk needs of one Transaction (docs/features/tax-year-events.md §5, R6): its ordering key, its
/// <c>bookedAt</c>, its type, whether it has been reversed, its legs on the Customer's Accounts, and its dividend and
/// in-kind transfer detail where it has one. Application fills it; Domain reads no store.
/// </summary>
public sealed record LedgerFact(
    int TransactionId,
    Guid TransactionPublicId,
    DateTimeOffset BookedAt,
    TransactionType Type,
    bool Reversed,
    IReadOnlyList<LedgerCashLeg> CashLegs,
    IReadOnlyList<LedgerSecurityLeg> SecurityLegs,
    LedgerDividend? Dividend = null,
    LedgerInKindTransfer? InKindTransfer = null,
    LedgerInterest? Interest = null);

/// <summary>A cash leg in the currency of its Account, signed as booked, with the kind of a <c>Tax</c> leg.</summary>
public sealed record LedgerCashLeg(
    int AccountId,
    CustodyMode CustodyMode,
    CashLegRole Role,
    decimal Amount,
    string Currency,
    TaxKind? TaxKind = null);

/// <summary>A security leg, signed as booked, with its cost in the quote currency. <see cref="LegId"/> orders the legs
/// of one Transaction.</summary>
public sealed record LedgerSecurityLeg(
    int LegId,
    int AccountId,
    CustodyMode CustodyMode,
    int InstrumentId,
    decimal Quantity,
    decimal CostAmount,
    string CostCurrency);

/// <summary>The dividend detail: the Instrument, the shares, the tax source and the imputation credit in the Account's
/// currency (dividends R39).</summary>
public sealed record LedgerDividend(int InstrumentId, decimal Shares, DividendTaxSource TaxSource, decimal ImputationCredit);

/// <summary>The in-kind transfer detail: whether the beneficial owner changes, and the stated acquisition date.</summary>
public sealed record LedgerInKindTransfer(bool ChangesBeneficialOwner, DateOnly? AcquiredOn);

/// <summary>The interest detail of an interest run's posting: the period it was paid for (interest-run R26;
/// docs/features/income-summary.md §5). A hand-entered <c>Interest</c> has none.</summary>
public sealed record LedgerInterest(DateOnly PeriodFrom, DateOnly PeriodTo, int Days);
