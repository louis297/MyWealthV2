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

    /// <summary><c>Principal</c> or <c>Fee</c> (trade-fees §8.1).</summary>
    public required string Role { get; init; }

    /// <summary>In the Account currency.</summary>
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>The fee detail on a <c>Fee</c> leg; null on a <c>Principal</c> leg (trade-fees §8.1).</summary>
    public TransactionFeeDto? Fee { get; init; }
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
