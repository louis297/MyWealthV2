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
        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return transactions
            .Where(row => includeClosedAccounts || !row.AccountClosed)
            .Where(row =>
            {
                var booked = row.BookedAt.ToUniversalTime();
                return booked >= fromUtc && booked < toUtc;
            })
            .ToList();
    }
}
