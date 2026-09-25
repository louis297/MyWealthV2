namespace MyWealthV2.Application.Transactions;

public sealed class TransactionDto
{
    public required Guid Id { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid AccountId { get; init; }

    public required Guid CustomerId { get; init; }

    public required string Type { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string BookedAt { get; init; }

    public string? Memo { get; init; }

    public string? Reference { get; init; }

    public Guid? OriginalTransactionId { get; init; }

    public required string RowVersion { get; init; }

    public static string FormatBookedAt(DateTimeOffset bookedAt) =>
        bookedAt.ToString("yyyy-MM-ddTHH:mm:sszzz");
}
