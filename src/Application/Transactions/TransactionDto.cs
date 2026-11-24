using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.Transactions;

public sealed class TransactionDto
{
    public required Guid Id { get; init; }

    public required Guid TenantId { get; init; }

    public required string Type { get; init; }

    /// <summary>The process that wrote the Transaction (transaction-source R10).</summary>
    public TransactionSourceDto? Source { get; init; }

    /// <summary>Cash legs on Accounts the caller can see. Empty when the type has no cash leg.</summary>
    public required IReadOnlyList<TransactionCashDto> Cash { get; init; }

    /// <summary>Serialised in UTC with Z by the webapi JSON options (fixes/utc-instants.md).</summary>
    public required DateTimeOffset BookedAt { get; init; }

    public string? Memo { get; init; }

    public string? Reference { get; init; }

    public Guid? OriginalTransactionId { get; init; }

    public Guid? ReversedByTransactionId { get; init; }

    public required string RowVersion { get; init; }

    /// <summary>Security legs on Accounts the caller can see. Empty when the type has no security leg.</summary>
    public required IReadOnlyList<TransactionSecurityDto> Security { get; init; }

    /// <summary>The rate the Transaction used, when it has an FX detail; otherwise null (FX conversions R22).</summary>
    public TransactionFxDto? Fx { get; init; }

    /// <summary>Why an adjustment was entered, on an <c>Adjustment</c> and its Reversal; otherwise null (adjustments R21).</summary>
    public TransactionAdjustmentDto? Adjustment { get; init; }

    /// <summary>The bank's details, on a deposit and on its Reversal; otherwise null (deposit-matching R36).</summary>
    public TransactionBankReceiptDto? BankReceipt { get; init; }

    /// <summary>The bank's details, on a payout and on its Reversal; otherwise null (withdrawals R41).</summary>
    public TransactionPaymentDto? Payment { get; init; }

    /// <summary>The period an interest posting pays, on the posting and its Reversal; otherwise null (interest-run R34).</summary>
    public TransactionInterestDto? Interest { get; init; }

    /// <summary>The event behind an entitlement posting, on the posting and its Reversal; otherwise null
    /// (corporate-actions R37).</summary>
    public TransactionCorporateActionDto? CorporateAction { get; init; }

    /// <summary>The broker's execution, on the Transaction of an order fill and on its Reversal; otherwise null
    /// (orders R38).</summary>
    public TransactionExecutionDto? Execution { get; init; }

    /// <summary>Where a migrated opening balance came from, on a Transaction a migration batch wrote; otherwise null
    /// (migration-batches R31).</summary>
    public TransactionMigrationDto? Migration { get; init; }

    /// <summary>The transfer behind a <c>StockIn</c> or <c>StockOut</c> an in-kind transfer wrote, and its Reversal;
    /// otherwise null (in-kind-transfers R42).</summary>
    public TransactionInKindTransferDto? InKindTransfer { get; init; }

    /// <summary>Dividends R50: the dividend behind a <c>Dividend</c> and its Reversal; otherwise null.</summary>
    public TransactionDividendDto? Dividend { get; init; }
}

/// <summary>Dividends R50: the corporate action, the instrument, the entitled quantity and the terms of a dividend,
/// with its reinvestment when there was one.</summary>
public sealed record TransactionDividendDto(
    Guid CorporateActionId,
    TransactionDividendInstrumentDto Instrument,
    decimal Quantity,
    decimal AmountPerShare,
    string Currency,
    string TaxSource,
    decimal ImputationCredit,
    decimal? ReinvestedQuantity,
    decimal? ReinvestmentPrice,
    string ExDate,
    string? RecordDate,
    string? PaymentDate);

public sealed record TransactionDividendInstrumentDto(Guid Id, string Symbol, string Name);

/// <summary>In-kind-transfers R42: the transfer, the counterparty with the account masked, and the custodian's side.</summary>
public sealed record TransactionInKindTransferDto(
    Guid TransferId,
    string Direction,
    string CounterpartyName,
    string CounterpartyAccount,
    bool ChangesBeneficialOwner,
    string? AcquiredOn,
    string SettlementDate,
    string CustodianReference);

/// <summary>Migration-batches R31: the date the previous provider's statement was taken and the provider.</summary>
public sealed record TransactionMigrationDto(string AsOfDate, string PreviousProvider);

/// <summary>An order fill's execution detail (orders R35, R38). <see cref="SettledAt"/> is read from the fill.</summary>
public sealed class TransactionExecutionDto
{
    public required Guid OrderId { get; init; }

    public required Guid FillId { get; init; }

    public required decimal Price { get; init; }

    public required string TradeDate { get; init; }

    public required string SettlementDate { get; init; }

    /// <summary>The broker's contract note; null when the broker sent none.</summary>
    public string? BrokerReference { get; init; }

    /// <summary>When the broker said the fill settled; null until then (R43).</summary>
    public DateTimeOffset? SettledAt { get; init; }

    /// <summary>Trade-settlement R34: when the broker took the fill back; null when it did not.</summary>
    public DateTimeOffset? BrokenAt { get; init; }
}

/// <summary>An entitlement posting's corporate action detail (corporate-actions R27, R37).</summary>
public sealed class TransactionCorporateActionDto
{
    public required Guid Id { get; init; }

    public required string Kind { get; init; }

    public required int RatioNew { get; init; }

    public required int RatioHeld { get; init; }

    public required string ExDate { get; init; }

    public string? RecordDate { get; init; }

    public string? PaymentDate { get; init; }

    public required decimal QuantityBefore { get; init; }

    public required decimal QuantityAfter { get; init; }
}

/// <summary>An interest posting's interest detail (interest-run R26, R34).</summary>
public sealed class TransactionInterestDto
{
    public required string PeriodFrom { get; init; }

    public required string PeriodTo { get; init; }

    public required int Days { get; init; }

    public required decimal AccruedAmount { get; init; }

    public required decimal GrossAmount { get; init; }
}

/// <summary>A <c>Tax</c> leg's tax detail (interest-run R25, R34). <see cref="Amount"/> is the tax, positive.</summary>
public sealed class TransactionTaxDto
{
    public required string Kind { get; init; }

    public required decimal RatePercent { get; init; }

    public required decimal BasisAmount { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }
}

/// <summary>A payout's payment detail (withdrawals R41). <see cref="PayeeAccount"/> is masked.</summary>
public sealed class TransactionPaymentDto
{
    public required string ValueDate { get; init; }

    public string? Reference { get; init; }

    public string? PayeeName { get; init; }

    public string? PayeeAccount { get; init; }
}

/// <summary>A deposit's bank receipt detail (deposit-matching R36). <see cref="PayerAccount"/> is masked.</summary>
public sealed class TransactionBankReceiptDto
{
    public required string ValueDate { get; init; }

    public string? Reference { get; init; }

    public string? PayerName { get; init; }

    public string? PayerAccount { get; init; }
}

/// <summary><see cref="Reason"/> is the <c>AdjustmentReason</c> name (adjustments R21).</summary>
public sealed class TransactionAdjustmentDto
{
    public required string Reason { get; init; }
}

/// <summary>
/// The source on the transaction item: <see cref="Kind"/> is the enum name (<c>Manual</c>, <c>Seed</c>,
/// <c>Rebalance</c>); <see cref="Id"/> is the source PublicId, or null (transaction-source R10).
/// </summary>
public sealed class TransactionSourceDto
{
    public required string Kind { get; init; }

    public Guid? Id { get; init; }
}

public sealed class TransactionCashDto
{
    public required Guid AccountId { get; init; }

    public required Guid CustomerId { get; init; }

    /// <summary><c>Principal</c>, <c>Fee</c> (trade-fees §8.1) or <c>Tax</c> (interest-run R34).</summary>
    public required string Role { get; init; }

    /// <summary>In the Account currency.</summary>
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>The fee detail on a <c>Fee</c> leg; null on a <c>Principal</c> leg (trade-fees §8.1).</summary>
    public TransactionFeeDto? Fee { get; init; }

    /// <summary>The tax detail on a <c>Tax</c> leg; null on any other leg (interest-run R34).</summary>
    public TransactionTaxDto? Tax { get; init; }
}

/// <summary>A <c>Fee</c> leg's fee detail. <see cref="Amount"/> is in the quote currency (trade-fees R18).</summary>
public sealed class TransactionFeeDto
{
    public required string Kind { get; init; }

    public Guid? ScheduleId { get; init; }

    public required decimal BasisAmount { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }
}

public sealed class TransactionSecurityDto
{
    public required Guid AccountId { get; init; }

    public required Guid CustomerId { get; init; }

    public required Guid InstrumentId { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal Cost { get; init; }

    public required string CostCurrency { get; init; }
}

public sealed class TransactionFxDto
{
    public required string BaseCurrency { get; init; }

    public required string QuoteCurrency { get; init; }

    public required decimal Rate { get; init; }

    public required string RateAsOfDate { get; init; }
}

internal static class TransactionSourceMapping
{
    public static TransactionSourceDto ToDto(TransactionSource source) => new()
    {
        Kind = source.Kind.ToString(),
        Id = source.PublicId
    };
}
