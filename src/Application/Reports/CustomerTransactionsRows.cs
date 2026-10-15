namespace MyWealthV2.Application.Reports;

public sealed record CustomerTransactionSource(
    int Id,
    int? OriginalTransactionId,
    DateTimeOffset BookedAt,
    bool AccountClosed,
    string AccountName,
    string Type,
    string? Memo);

public static class CustomerTransactionsRows
{
    public static IReadOnlyList<CustomerTransactionSource> Select(
        IReadOnlyList<CustomerTransactionSource> transactions,
        DateOnly from,
        DateOnly to,
        bool includeClosedAccounts)
    {
        _ = transactions;
        _ = from;
        _ = to;
        _ = includeClosedAccounts;
        return [];
    }
}
