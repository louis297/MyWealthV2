namespace MyWealthV2.Domain.Tax;

/// <summary>
/// One <c>Interest</c> Transaction of the Customer (docs/features/income-summary.md R3, §5): its place in the order of
/// tax-year-events R8, its <c>bookedAt</c>, the Account of its <c>Principal</c> leg and that Account's custody mode and
/// currency, the gross signed as booked, the three taxes as positive figures, and the period of an interest run's
/// posting (none on a hand-entered one). All figures are in the Account's currency.
/// </summary>
public sealed record TaxInterest(
    int Ordinal,
    int TransactionId,
    Guid TransactionPublicId,
    DateTimeOffset BookedAt,
    int AccountId,
    CustodyMode CustodyMode,
    string Currency,
    decimal Gross,
    decimal Rwt,
    decimal Nrwt,
    decimal ForeignTax,
    LedgerInterest? Period);
