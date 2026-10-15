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
        var included = transactions
            .Where(row => includeClosedAccounts || !row.AccountClosed)
            .Where(row =>
            {
                var booked = row.BookedAt.ToUniversalTime();
                return booked >= fromUtc && booked < toUtc;
            })
            .ToList();
        var includedIds = included.Select(row => row.Id).ToHashSet();
        var reversalByOriginal = new Dictionary<int, CustomerTransactionSource>();
        foreach (var row in included)
        {
            if (row.OriginalTransactionId is int originalId && includedIds.Contains(originalId))
            {
                reversalByOriginal[originalId] = row;
            }
        }

        var ordered = new List<CustomerTransactionSource>();
        foreach (var anchor in included
                     .Where(row => row.OriginalTransactionId is not int originalId || !includedIds.Contains(originalId))
                     .OrderBy(row => row.BookedAt.ToUniversalTime())
                     .ThenBy(row => row.Id))
        {
            ordered.Add(anchor);
            if (reversalByOriginal.TryGetValue(anchor.Id, out var reversal))
            {
                ordered.Add(reversal);
            }
        }

        return ordered;
    }
}
