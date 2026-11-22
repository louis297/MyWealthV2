using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.CorporateActions;

/// <summary>Corporate-actions R9–R11: a stored announcement and the cursor of the feed it came from.</summary>
public class CorporateActionAnnouncementTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 22, 3, 15, 2, TimeSpan.Zero);
    private static readonly DateOnly ExDate = new(2026, 11, 24);

    private static CorporateActionAnnouncement Receive(CorporateActionKind? kind = CorporateActionKind.Split, string currency = "usd") =>
        CorporateActionAnnouncement.Receive(
            "simulator", "w-CA1-V1", "w-CA1", 1, PartyActionStatus.Announced, "ACME.SIM", currency, kind, 2, 1, ExDate, null, null, At);

    [Test]
    public void Receive_IsReceived_WithTheFieldsAndAnUpperCaseCurrency()
    {
        var row = Receive();

        row.PublicId.ShouldNotBe(Guid.Empty);
        (row.Connection, row.OutsideId, row.PartyActionId, row.Version, row.PartyStatus, row.Symbol, row.Currency, row.Kind)
            .ShouldBe(("simulator", "w-CA1-V1", "w-CA1", 1, PartyActionStatus.Announced, "ACME.SIM", "USD", (CorporateActionKind?)CorporateActionKind.Split));
        (row.RatioNew, row.RatioHeld, row.ExDate, row.ReceivedAt, row.Status).ShouldBe((2, 1, ExDate, At, AnnouncementStatus.Received));
        row.SkipReason.ShouldBeNull();
        row.ProcessedAt.ShouldBeNull();
    }

    [Test]
    public void Receive_AnUnknownKind_KeepsNoKind() => Receive(kind: null).Kind.ShouldBeNull();

    [TestCase(null, "w-CA1", 1, "ACME.SIM", "USD", 2, 1)]
    [TestCase("w-CA1-V1", "", 1, "ACME.SIM", "USD", 2, 1)]
    [TestCase("w-CA1-V1", "w-CA1", 0, "ACME.SIM", "USD", 2, 1)]
    [TestCase("w-CA1-V1", "w-CA1", 1, "", "USD", 2, 1)]
    [TestCase("w-CA1-V1", "w-CA1", 1, "ACME.SIM", "US1", 2, 1)]
    [TestCase("w-CA1-V1", "w-CA1", 1, "ACME.SIM", "USD", 0, 1)]
    [TestCase("w-CA1-V1", "w-CA1", 1, "ACME.SIM", "USD", 2, 0)]
    public void IsStorable_RefusesAMissingOrBadField(
        string? outsideId, string? partyActionId, int version, string symbol, string currency, int ratioNew, int ratioHeld) =>
        CorporateActionAnnouncement.IsStorable(outsideId, partyActionId, version, PartyActionStatus.Announced, symbol, currency,
            ratioNew, ratioHeld, ExDate).ShouldBeFalse();

    [Test]
    public void IsStorable_NeedsAStatusAndAnExDate()
    {
        CorporateActionAnnouncement.IsStorable("a", "b", 1, null, "S", "USD", 1, 1, ExDate).ShouldBeFalse();
        CorporateActionAnnouncement.IsStorable("a", "b", 1, PartyActionStatus.Cancelled, "S", "USD", 1, 1, null).ShouldBeFalse();
        CorporateActionAnnouncement.IsStorable("a", "b", 1, PartyActionStatus.Cancelled, "S", "USD", 1, 1, ExDate).ShouldBeTrue();
    }

    [Test]
    public void MarkProcessed_AndSkip_HappenOnce()
    {
        var processed = Receive();
        processed.MarkProcessed(At.AddSeconds(1));
        (processed.Status, processed.ProcessedAt).ShouldBe((AnnouncementStatus.Processed, (DateTimeOffset?)At.AddSeconds(1)));
        Should.Throw<DomainException>(() => processed.Skip(AnnouncementSkipReason.Superseded, At));

        var skipped = Receive();
        skipped.Skip(AnnouncementSkipReason.NoInstrument, At);
        (skipped.Status, skipped.SkipReason).ShouldBe((AnnouncementStatus.Skipped, (AnnouncementSkipReason?)AnnouncementSkipReason.NoInstrument));
        Should.Throw<DomainException>(() => skipped.MarkProcessed(At));
    }

    [Test]
    public void TheFeedCursor_Advances_KeepsAMissingCursor_AndRefusesOneOver500()
    {
        var feed = MarketDataFeed.Start("simulator", MarketDataFeedKind.CorporateActions);

        feed.Advance("w:3", At);
        (feed.Cursor, feed.LastSucceededAt).ShouldBe(("w:3", (DateTimeOffset?)At));
        feed.Advance(null, At.AddMinutes(1));
        feed.Cursor.ShouldBe("w:3");
        Should.Throw<DomainException>(() => feed.Advance(new string('c', 501), At));
        feed.Cursor.ShouldBe("w:3");
    }
}
