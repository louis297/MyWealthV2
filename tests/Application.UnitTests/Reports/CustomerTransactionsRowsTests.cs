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

    [Test]
    public void Select_OmitsClosedAccountsUnlessRequested()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 1, 3);
        var booked = new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero);
        var rows = new[]
        {
            Row(1, booked, memo: "open"),
            Row(2, booked, accountClosed: true, memo: "closed")
        };

        CustomerTransactionsRows.Select(rows, from, to, includeClosedAccounts: false)
            .Select(row => row.Memo)
            .ShouldBe(["open"]);
        CustomerTransactionsRows.Select(rows, from, to, includeClosedAccounts: true)
            .Select(row => row.Memo)
            .ShouldBe(["open", "closed"]);
    }

    [Test]
    public void Select_PlacesReversalImmediatelyAfterItsOriginal()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 2, 1);
        var rows = new[]
        {
            Row(2, At(2026, 1, 20), originalTransactionId: 1, memo: "reversal-of-in-range"),
            Row(1, At(2026, 1, 2), memo: "original"),
            Row(4, At(2026, 1, 15), originalTransactionId: 99, memo: "orphan-reversal"),
            Row(5, At(2026, 1, 10), memo: "middle"),
            Row(99, At(2025, 12, 1), memo: "outside-original")
        };

        var selected = CustomerTransactionsRows.Select(rows, from, to, includeClosedAccounts: false);

        selected.Select(row => row.Memo).ShouldBe([
            "original",
            "reversal-of-in-range",
            "middle",
            "orphan-reversal"
        ]);
    }

    private static DateTimeOffset At(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static CustomerTransactionSource Row(
        int id,
        DateTimeOffset bookedAt,
        int? originalTransactionId = null,
        bool accountClosed = false,
        string? memo = null) =>
        new(id, originalTransactionId, bookedAt, accountClosed, "Cash", "TransferIn", memo);
}
