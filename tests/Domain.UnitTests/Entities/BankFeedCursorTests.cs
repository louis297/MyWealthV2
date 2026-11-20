using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Deposit matching R4, R7–R9: one cursor and feed status per client money account.</summary>
public class BankFeedCursorTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 20, 2, 1, 7, TimeSpan.Zero);

    [Test]
    public void Start_HasNoCursorAndNoStatus()
    {
        var cursor = BankFeedCursor.Start(7);

        cursor.ClientMoneyAccountId.ShouldBe(7);
        cursor.Cursor.ShouldBeNull();
        cursor.LastSucceededAt.ShouldBeNull();
        cursor.LastFailedAt.ShouldBeNull();
        cursor.LastFailure.ShouldBeNull();
    }

    [Test]
    public void Advance_SetsTheCursorAndTheSuccessTime_AndKeepsTheLastFailure()
    {
        var cursor = BankFeedCursor.Start(7);
        cursor.Fail(BankFeedFailure.Unavailable, At);

        cursor.Advance("w:12", At.AddMinutes(1));

        cursor.Cursor.ShouldBe("w:12");
        cursor.LastSucceededAt.ShouldBe(At.AddMinutes(1));
        cursor.LastFailedAt.ShouldBe(At);
        cursor.LastFailure.ShouldBe(BankFeedFailure.Unavailable);
    }

    [Test]
    public void Fail_KeepsTheCursor()
    {
        var cursor = BankFeedCursor.Start(7);
        cursor.Advance("w:12", At);

        cursor.Fail(BankFeedFailure.Timeout, At.AddMinutes(1));

        cursor.Cursor.ShouldBe("w:12");
        cursor.LastSucceededAt.ShouldBe(At);
        cursor.LastFailedAt.ShouldBe(At.AddMinutes(1));
        cursor.LastFailure.ShouldBe(BankFeedFailure.Timeout);
    }

    [Test]
    public void Advance_RefusesACursorOver500()
    {
        var cursor = BankFeedCursor.Start(7);

        Should.Throw<DomainException>(() => cursor.Advance(new string('c', 501), At));
        cursor.Advance(new string('c', 500), At);
        cursor.Cursor!.Length.ShouldBe(500);
    }
}
