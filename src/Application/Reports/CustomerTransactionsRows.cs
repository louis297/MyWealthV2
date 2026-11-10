namespace MyWealthV2.Application.Reports;

public sealed record CustomerTransactionSource(
    int Id,
    int? OriginalTransactionId,
    DateTimeOffset BookedAt,
    bool AccountClosed,
    string AccountName,
    string Type,
    string? Memo,
    int AccountId);

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
        // One row per (transaction, leg account): a reversal row pairs with the original row on the same account.
        var includedKeys = included.Select(row => (row.Id, row.AccountId)).ToHashSet();
        var reversalByOriginal = new Dictionary<(int Id, int AccountId), CustomerTransactionSource>();
        foreach (var row in included)
        {
            if (row.OriginalTransactionId is int originalId && includedKeys.Contains((originalId, row.AccountId)))
            {
                reversalByOriginal[(originalId, row.AccountId)] = row;
            }
        }

        var ordered = new List<CustomerTransactionSource>();
        foreach (var anchor in included
                     .Where(row => row.OriginalTransactionId is not int originalId
                                   || !includedKeys.Contains((originalId, row.AccountId)))
                     .OrderBy(row => row.BookedAt.ToUniversalTime())
                     .ThenBy(row => row.Id)
                     .ThenBy(row => row.AccountId))
        {
            ordered.Add(anchor);
            if (reversalByOriginal.TryGetValue((anchor.Id, anchor.AccountId), out var reversal))
            {
                ordered.Add(reversal);
            }
        }

        return ordered;
    }
}
