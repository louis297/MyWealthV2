using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Tax-year-events R3, R4: the two instants of a tax year, the year of an instant, and which years can be read.</summary>
public class TaxYearTests
{
    [TestCase(2026, "2025-03-31T11:00:00Z", "2026-03-31T11:00:00Z")]
    [TestCase(2027, "2026-03-31T11:00:00Z", "2027-03-31T11:00:00Z")]
    public void StartAndEnd_Are1100UtcOn31March(int taxYear, string start, string end)
    {
        TaxYear.Start(taxYear).ShouldBe(DateTimeOffset.Parse(start));
        TaxYear.End(taxYear).ShouldBe(DateTimeOffset.Parse(end));
        TaxYear.Start(taxYear).Offset.ShouldBe(TimeSpan.Zero);
    }

    [TestCase(2009)]
    [TestCase(2026)]
    [TestCase(2040)]
    public void End_IsTheStartOfTheNextYear(int taxYear)
    {
        TaxYear.End(taxYear).ShouldBe(TaxYear.Start(taxYear + 1));
    }

    [Test]
    public void Of_ATickBeforeAtAndATickAfterAStart()
    {
        var start = TaxYear.Start(2026);

        TaxYear.Of(start.AddTicks(-1)).ShouldBe(2025);
        TaxYear.Of(start).ShouldBe(2026);
        TaxYear.Of(start.AddTicks(1)).ShouldBe(2026);
    }

    [Test]
    public void TenFiftyNineAndElevenUtcOn31March_AreTwoTaxYears()
    {
        TaxYear.Of(new DateTimeOffset(2025, 3, 31, 10, 59, 0, TimeSpan.Zero)).ShouldBe(2025);
        TaxYear.Of(new DateTimeOffset(2025, 3, 31, 11, 0, 0, TimeSpan.Zero)).ShouldBe(2026);
    }

    [Test]
    public void Of_ReadsTheInstantInUtc()
    {
        // 09:30 on 1 April 2025 in Auckland is 20:30 UTC on 31 March: tax year 2026.
        TaxYear.Of(DateTimeOffset.Parse("2025-04-01T09:30:00+13:00")).ShouldBe(2026);
        TaxYear.Of(DateTimeOffset.Parse("2025-03-31T23:30:00+13:00")).ShouldBe(2025);
        TaxYear.Of(new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero)).ShouldBe(2026);
        TaxYear.Of(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).ShouldBe(2026);
    }

    [Test]
    public void TheValuationDays_AreThe31MarchAtEitherEnd()
    {
        TaxYear.OpeningDay(2026).ShouldBe(new DateOnly(2025, 3, 31));
        TaxYear.ClosingDay(2026).ShouldBe(new DateOnly(2026, 3, 31));
    }

    [TestCase(2008, "2026-11-28", false)]
    [TestCase(2009, "2026-11-28", true)]
    [TestCase(2026, "2026-11-28", true)]
    [TestCase(2027, "2026-11-28", false)]
    [TestCase(2026, "2026-03-31", false)]
    [TestCase(2026, "2026-04-01", false)]
    [TestCase(2026, "2026-04-02", true)]
    public void IsReadable_From2009_AndFrom2AprilOfTheYear(int taxYear, string today, bool readable)
    {
        TaxYear.IsReadable(taxYear, DateOnly.Parse(today)).ShouldBe(readable);
    }
}
