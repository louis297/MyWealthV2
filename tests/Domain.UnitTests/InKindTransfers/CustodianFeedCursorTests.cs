using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.InKindTransfers;

/// <summary>In-kind transfers R10, R12: the custodian feed cursor of a safekeeping account.</summary>
public class CustodianFeedCursorTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 24, 1, 0, 0, TimeSpan.Zero);

    [Test]
    public void Start_HasNothingYet()
    {
        var cursor = CustodianFeedCursor.Start(3, 9);

        (cursor.TenantId, cursor.SafekeepingAccountId).ShouldBe((3, 9));
        cursor.Cursor.ShouldBeNull();
        cursor.PositionsAt.ShouldBeNull();
        cursor.LastSucceededAt.ShouldBeNull();
    }

    [Test]
    public void StorePage_MovesTheCursor_AndSetsThePositionsTime()
    {
        var cursor = CustodianFeedCursor.Start(3, 9);

        cursor.StorePage("w:12", At);

        (cursor.Cursor, cursor.LastSucceededAt, cursor.PositionsAt).ShouldBe(("w:12", (DateTimeOffset?)At, (DateTimeOffset?)At));
    }

    [Test]
    public void StorePage_RefusesACursorOver500() =>
        Should.Throw<DomainException>(() => CustodianFeedCursor.Start(3, 9).StorePage(new string('c', 501), At));

    [Test]
    public void RecordFailure_KeepsTheCursorAndThePositionsTime()
    {
        var cursor = CustodianFeedCursor.Start(3, 9);
        cursor.StorePage("w:12", At);

        cursor.RecordFailure(CustodianFailure.Timeout, At.AddMinutes(1));

        (cursor.Cursor, cursor.PositionsAt).ShouldBe(("w:12", (DateTimeOffset?)At));
        (cursor.LastFailure, cursor.LastFailedAt).ShouldBe(((CustodianFailure?)CustodianFailure.Timeout, (DateTimeOffset?)At.AddMinutes(1)));
    }
}
