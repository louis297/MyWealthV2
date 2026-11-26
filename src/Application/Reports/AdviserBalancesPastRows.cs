using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.Reports;

/// <summary>One account of the Adviser Balances Report for the end of a past day (valuation-history R17): its cash and
/// valuation lines of that day, or <see cref="NotValuedYet"/> for a Custodial account with no stored valuation.</summary>
public sealed record AdviserPastAccount(
    Guid CustomerId,
    string CustomerName,
    Guid AccountId,
    string AccountName,
    string AccountType,
    string CustodyMode,
    string Currency,
    bool NotValuedYet,
    decimal Cash,
    IReadOnlyList<AdviserPastHolding> Holdings);

/// <summary>One valuation line of a past day: the quantity (restated or not), the price row and its date, and the
/// market value in the quote currency when the line has one.</summary>
public sealed record AdviserPastHolding(
    Guid InstrumentId,
    string Symbol,
    decimal Quantity,
    decimal LedgerQuantity,
    ValuationLineStatus Status,
    decimal? PriceAmount,
    DateOnly? PriceDate,
    decimal? MarketValue,
    string QuoteCurrency);

/// <summary>A holding line of a past day (R18). <see cref="MarketValue"/> is null when the line was not valued.</summary>
public sealed record AdviserPastHoldingLine(
    Guid CustomerId,
    string CustomerName,
    Guid AccountId,
    string AccountName,
    string CustodyMode,
    Guid InstrumentId,
    string Symbol,
    decimal Quantity,
    decimal LedgerQuantity,
    ValuationLineStatus Status,
    decimal? PriceAmount,
    DateOnly? PriceDate,
    decimal? MarketValue,
    string MarketCurrency) : AdviserBalanceLine(CustomerId, CustomerName, AccountId, AccountName, CustodyMode);

/// <summary>A Custodial account expected on the day with no stored valuation of it (R17): "Not valued yet".</summary>
public sealed record AdviserNotValuedLine(
    Guid CustomerId,
    string CustomerName,
    Guid AccountId,
    string AccountName,
    string AccountType,
    string CustodyMode) : AdviserBalanceLine(CustomerId, CustomerName, AccountId, AccountName, CustodyMode);

public sealed record AdviserBalancesPastSelection(
    IReadOnlyList<AdviserBalanceLine> Lines,
    IReadOnlyList<AdviserBalanceSubtotal> Subtotals,
    IReadOnlyList<AdviserBalanceModeSubtotals> ModeSubtotals,
    int HoldingsNotValued,
    int AccountsNotValuedYet);

/// <summary>Valuation-history R17–R19: the lines and subtotals of the Adviser Balances Report for a past date.</summary>
public static class AdviserBalancesPastRows
{
    public static AdviserBalancesPastSelection Select(IReadOnlyList<AdviserPastAccount> accounts) =>
        new([], [], [], 0, 0);

    /// <summary>R18: the label of a line that is not valued; none for a valued line or one with no rate.</summary>
    public static string? StatusLabel(ValuationLineStatus status) => null;
}
