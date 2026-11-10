namespace MyWealthV2.Application.Transactions;

public sealed class TransactionDto
{
    public required Guid Id { get; init; }

    public required Guid TenantId { get; init; }

    public required string Type { get; init; }

    /// <summary>Cash legs on Accounts the caller can see. Empty when the type has no cash leg.</summary>
    public required IReadOnlyList<TransactionCashDto> Cash { get; init; }

    public required string BookedAt { get; init; }

    public string? Memo { get; init; }

    public string? Reference { get; init; }

    public Guid? OriginalTransactionId { get; init; }

    public Guid? ReversedByTransactionId { get; init; }

    public required string RowVersion { get; init; }

    /// <summary>Security legs on Accounts the caller can see. Empty when the type has no security leg.</summary>
    public required IReadOnlyList<TransactionSecurityDto> Security { get; init; }

    /// <summary>The rate the Transaction used, when it has an FX detail; otherwise null (FX conversions R22).</summary>
    public TransactionFxDto? Fx { get; init; }

    public static string FormatBookedAt(DateTimeOffset bookedAt) =>
        bookedAt.ToString("yyyy-MM-ddTHH:mm:sszzz");
}

public sealed class TransactionCashDto
{
    public required Guid AccountId { get; init; }

    public required Guid CustomerId { get; init; }

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
