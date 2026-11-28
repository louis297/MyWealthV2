using MyWealthV2.Application.Valuations;

namespace MyWealthV2.Application.TaxYears;

/// <summary>The item of <c>GET /users/customers/{customerId}/tax-years/{taxYear}</c> (tax-year-events R31).</summary>
public sealed record CustomerTaxYearDto(
    Guid CustomerId,
    int TaxYear,
    DateTimeOffset From,
    DateTimeOffset To,
    string Currency,
    DateTimeOffset LedgerReadAt,
    IReadOnlyList<TaxHoldingDto> Holdings,
    bool Complete,
    int IncompleteCount,
    ThresholdDto Threshold,
    FifIncomeDto FifIncome);

/// <summary>FIF income R26: the totals of both methods over the computed tax holdings, or <c>null</c> when a tax holding
/// that belongs in them is missing (R23). Every amount is in NZD.</summary>
public sealed record FifIncomeDto(
    decimal Rate,
    FifFdrTotalsDto? Fdr,
    FifCvTotalsDto? Cv,
    string? Lower,
    bool Complete,
    IReadOnlyList<FifIncomeIncompleteDto> Incomplete);

public sealed record FifFdrTotalsDto(
    decimal OpeningValue,
    decimal Amount,
    decimal PeakHoldingAmount,
    decimal QuickSaleGainAmount,
    decimal QuickSaleAdjustment,
    decimal Income);

public sealed record FifCvTotalsDto(decimal Amount, decimal Income);

/// <summary>R23: a tax holding that is not computed.</summary>
public sealed record FifIncomeIncompleteDto(string Reason, Guid InstrumentId);

/// <summary>R26: the FIF income of one tax holding; the figures are <c>null</c> unless its status is <c>Computed</c>.</summary>
public sealed record TaxHoldingIncomeDto(string Status, string? Reason, FifFdrDto? Fdr, QuickSaleDto? QuickSale, FifCvDto? Cv);

public sealed record FifFdrDto(decimal OpeningValue, decimal Amount);

/// <summary>R9–R14: the quick sale of a tax holding. Every quantity is a comparable quantity.</summary>
public sealed record QuickSaleDto(
    decimal OpeningQuantity,
    decimal PeakQuantity,
    decimal ClosingQuantity,
    decimal PeakHoldingDifferential,
    decimal PurchasedQuantity,
    decimal PurchaseCost,
    decimal? AverageCost,
    decimal PeakHoldingAmount,
    decimal MatchedQuantity,
    decimal Proceeds,
    decimal Dividends,
    decimal Cost,
    decimal GainAmount,
    IReadOnlyList<QuickSaleMatchDto> Matches,
    IReadOnlyList<QuickSaleSaleDto> Sales,
    IReadOnlyList<QuickSaleDividendShareDto> DividendShares);

/// <summary>R13: shares one sale took from one purchase, by the <c>sequence</c> of each in <c>events</c>.</summary>
public sealed record QuickSaleMatchDto(int PurchaseSequence, int SaleSequence, decimal Quantity);

public sealed record QuickSaleSaleDto(int Sequence, decimal Quantity, decimal MatchedQuantity, decimal Amount, decimal Proceeds);

public sealed record QuickSaleDividendShareDto(int Sequence, decimal Amount, decimal HeldQuantity, decimal MatchedQuantity, decimal Share);

/// <summary>R16: the figures of the CV amount of a tax holding.</summary>
public sealed record FifCvDto(decimal OpeningValue, decimal ClosingValue, decimal Sales, decimal Dividends, decimal Purchases, decimal Amount);

/// <summary>Tax-cost-threshold R24: the threshold test of the year. Every figure is in NZD.</summary>
public sealed record ThresholdDto(
    decimal? Amount,
    decimal Opening,
    ThresholdPeakDto Peak,
    decimal Closing,
    string Outcome,
    bool Complete,
    IReadOnlyList<ThresholdIncompleteDto> Incomplete,
    IReadOnlyList<ThresholdStepDto> Steps);

/// <summary>R18: the highest total; the Instrument and the Transaction are <c>null</c> when it is the opening.</summary>
public sealed record ThresholdPeakDto(decimal Total, DateTimeOffset At, Guid? InstrumentId, Guid? TransactionId);

/// <summary>R20: a reason the test is incomplete; <c>NoThreshold</c> names no Instrument.</summary>
public sealed record ThresholdIncompleteDto(string Reason, Guid? InstrumentId);

/// <summary>R24: an event of the year that changed the total or made a cost unknown.</summary>
public sealed record ThresholdStepDto(
    DateTimeOffset At, Guid InstrumentId, int EventSequence, Guid TransactionId, string Kind, decimal? Change, decimal Total);

/// <summary>R24: the tax cost of a tax holding; <c>unknown</c> is the first point it could not be worked out.</summary>
public sealed record TaxCostDto(string Status, decimal? Opening, decimal? Closing, TaxCostUnknownDto? Unknown);

public sealed record TaxCostUnknownDto(string Cause, DateTimeOffset At, Guid TransactionId);

/// <summary>R24: the tax cost of an event; <c>fx</c> is the rate row of a carried cost or a correction.</summary>
public sealed record TaxEventCostDto(decimal? Change, decimal? After, AccountValuationFxDto? Fx);

/// <summary>One tax holding: an Instrument over all the Customer's Accounts in the year.</summary>
public sealed record TaxHoldingDto(
    TaxInstrumentDto Instrument,
    TaxTreatmentDto Treatment,
    TaxPointDto Opening,
    TaxPointDto Closing,
    decimal? PeakQuantity,
    IReadOnlyList<TaxEventDto> Events,
    bool Complete,
    IReadOnlyList<string> Incomplete,
    IReadOnlyList<string> Notices,
    TaxCostDto Cost,
    TaxHoldingIncomeDto FifIncome);

public sealed record TaxInstrumentDto(Guid Id, string Symbol, string Name, string QuoteCurrency);

/// <summary>R26: the treatment for the year, the tax year of the row it came from, its note and the issuer country.</summary>
public sealed record TaxTreatmentDto(string FifTreatment, int? RowTaxYear, string? Note, string? IssuerCountry);

/// <summary>The opening or the closing (R22, R23).</summary>
public sealed record TaxPointDto(
    DateTimeOffset At,
    DateOnly ValuationDay,
    decimal Quantity,
    decimal? ComparableQuantity,
    decimal? Value,
    string Status,
    IReadOnlyList<TaxPointLineDto> Lines);

/// <summary>One Account's line of an opening or a closing: the fields of account-valuation R21, the value in NZD.</summary>
public sealed record TaxPointLineDto(
    Guid AccountId,
    string CustodyMode,
    decimal Quantity,
    decimal LedgerQuantity,
    string Status,
    AccountValuationPriceDto? Price,
    decimal? MarketValue,
    AccountValuationFxDto? Fx,
    decimal? Value);

/// <summary>One tax event of the year. A field its kind does not have is <c>null</c>.</summary>
public sealed record TaxEventDto(
    int Sequence,
    string Kind,
    DateTimeOffset BookedAt,
    Guid TransactionId,
    Guid AccountId,
    string CustodyMode,
    decimal Quantity,
    decimal QuantityAfter,
    decimal? ComparableQuantityAfter,
    decimal? Amount,
    string Status,
    object? Ledger,
    AccountValuationFxDto? Fx,
    AccountValuationPriceDto? Price,
    decimal? Principal,
    decimal? Fee,
    TaxDividendDto? Dividend,
    TaxCarriedCostDto? CarriedCost,
    TaxMoveDto? Move,
    TaxEventCostDto? Cost);

/// <summary>The figures of a trade in the Account's currency.</summary>
public sealed record TaxTradeLedgerDto(string Currency, decimal Principal, decimal Fee);

/// <summary>The figures of a dividend in the Account's currency.</summary>
public sealed record TaxDividendLedgerDto(
    string Currency, decimal Gross, decimal ForeignTax, decimal Rwt, decimal Nrwt, decimal ImputationCredit);

/// <summary>The figures of a dividend in NZD.</summary>
public sealed record TaxDividendDto(
    decimal Shares,
    string TaxSource,
    decimal? Gross,
    decimal? ForeignTax,
    decimal? Rwt,
    decimal? Nrwt,
    decimal? ImputationCredit);

/// <summary>R14, R16: the cost a leg carries, unconverted.</summary>
public sealed record TaxCarriedCostDto(decimal Amount, string Currency, DateOnly? AcquiredOn);

/// <summary>R15: the two legs of a move.</summary>
public sealed record TaxMoveDto(
    decimal Quantity,
    Guid FromAccountId,
    Guid FromTransactionId,
    DateTimeOffset FromBookedAt,
    Guid ToAccountId,
    Guid ToTransactionId,
    DateTimeOffset ToBookedAt);
