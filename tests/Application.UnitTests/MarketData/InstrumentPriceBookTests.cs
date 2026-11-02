using MyWealthV2.Application.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.MarketData;

public class InstrumentPriceBookTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    [Test]
    public void Select_Missing_IsEmpty()
    {
        InstrumentPriceBook.Select(42, Today, []).ShouldBeNull();
    }

    [Test]
    public void Select_UsesTheLatestRowOnOrBeforeTheRequestedDate()
    {
        var rows = new InstrumentPriceRow[]
        {
            new(7, 250m, new DateOnly(2026, 1, 1), default),
            new(7, 260m, new DateOnly(2026, 10, 11), default),
            new(8, 8.5m, new DateOnly(2026, 11, 2), default)
        };

        InstrumentPriceBook.Select(7, new DateOnly(2026, 10, 11), rows).ShouldBe(260m);
        InstrumentPriceBook.Select(7, new DateOnly(2026, 10, 10), rows).ShouldBe(250m);
        InstrumentPriceBook.Select(7, Today, rows).ShouldBe(260m);
        InstrumentPriceBook.Select(7, new DateOnly(2025, 12, 31), rows).ShouldBeNull();
        InstrumentPriceBook.Select(8, Today, rows).ShouldBe(8.5m);
        InstrumentPriceBook.Select(9, Today, rows).ShouldBeNull();
    }

    [Test]
    public void Upsert_SameDay_UpdatesPriceAndFetchedAt()
    {
        var firstFetched = new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero);
        var secondFetched = new DateTimeOffset(2026, 11, 2, 2, 0, 0, TimeSpan.Zero);
        var asOf = new DateOnly(2026, 11, 1);
        var existing = new InstrumentPriceRow[]
        {
            new(7, 250m, asOf, firstFetched),
            new(8, 8.5m, asOf, firstFetched)
        };

        var updated = InstrumentPriceBook.Upsert(existing, 7, 251m, asOf, secondFetched);

        updated.Count.ShouldBe(2);
        var row = updated.Single(candidate => candidate.InstrumentId == 7 && candidate.AsOfDate == asOf);
        row.Price.ShouldBe(251m);
        row.FetchedAt.ShouldBe(secondFetched);
        var other = updated.Single(candidate => candidate.InstrumentId == 8);
        other.Price.ShouldBe(8.5m);
        other.FetchedAt.ShouldBe(firstFetched);
    }

    [Test]
    public void Upsert_NewDate_InsertsWithoutRemovingThePreviousDate()
    {
        var firstFetched = new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero);
        var secondFetched = new DateTimeOffset(2026, 11, 2, 2, 0, 0, TimeSpan.Zero);
        var existing = new InstrumentPriceRow[]
        {
            new(7, 250m, new DateOnly(2026, 11, 1), firstFetched)
        };

        var updated = InstrumentPriceBook.Upsert(
            existing, 7, 260m, new DateOnly(2026, 11, 2), secondFetched);

        updated.Count.ShouldBe(2);
        updated.Single(candidate => candidate.AsOfDate == new DateOnly(2026, 11, 1)).Price.ShouldBe(250m);
        var inserted = updated.Single(candidate => candidate.AsOfDate == new DateOnly(2026, 11, 2));
        inserted.InstrumentId.ShouldBe(7);
        inserted.Price.ShouldBe(260m);
        inserted.FetchedAt.ShouldBe(secondFetched);
    }
}
