using MyWealthV2.Application.Reports;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Reports;

public class CustomerTransactionsRowsTests
{
    [Test]
    public void Select_IncludesBookedAtInHalfOpenUtcDay()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 1, 3);
        var rows = new[]
        {
            Row(1, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), memo: "start"),
            Row(2, new DateTimeOffset(2026, 1, 2, 23, 59, 59, TimeSpan.Zero), memo: "end-of-second"),
            Row(3, new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero), memo: "to-day"),
            Row(4, new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(13)), memo: "previous-utc-day"),
            Row(5, new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero), memo: "before")
        };

        var selected = CustomerTransactionsRows.Select(rows, from, to, includeClosedAccounts: false);

        selected.Select(row => row.Memo).ShouldBe(["start", "end-of-second"]);
    }

    private static CustomerTransactionSource Row(
        int id,
        DateTimeOffset bookedAt,
        int? originalTransactionId = null,
        bool accountClosed = false,
        string? memo = null) =>
        new(id, originalTransactionId, bookedAt, accountClosed, "Cash", "TransferIn", memo);
}
