using MyWealthV2.Domain.Instruments;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Instruments;

public class CountriesTests
{
    [TestCase("NZ")]
    [TestCase("AU")]
    [TestCase("US")]
    [TestCase("GB")]
    [TestCase("DE")]
    [TestCase("FR")]
    [TestCase("NL")]
    [TestCase("JP")]
    public void IsKnown_AcceptsAnAssignedCode(string code) => Countries.IsKnown(code).ShouldBeTrue();

    [TestCase("UK")]
    [TestCase("XX")]
    [TestCase("nz")]
    [TestCase("Nz")]
    [TestCase("NZL")]
    [TestCase("")]
    [TestCase(" NZ")]
    [TestCase(null)]
    public void IsKnown_RefusesAnythingElse(string? code) => Countries.IsKnown(code).ShouldBeFalse();

    [Test]
    public void All_HoldsTheOfficiallyAssignedCodes() => Countries.All.Count.ShouldBe(249);

    [Test]
    public void All_EveryCodeIsTwoUpperCaseLettersAndAppearsOnce()
    {
        Countries.All.ShouldNotBeEmpty();
        Countries.All.ShouldAllBe(country => country.Code.Length == 2 && country.Code.All(char.IsAsciiLetterUpper));
        Countries.All.Select(country => country.Code).Distinct().Count().ShouldBe(Countries.All.Count);
    }

    [Test]
    public void All_EveryEntryHasAName()
    {
        Countries.All.ShouldNotBeEmpty();
        Countries.All.ShouldAllBe(country => !string.IsNullOrWhiteSpace(country.Name));
    }

    [Test]
    public void All_NamesTheCountriesOfTheDemoCatalog()
    {
        Countries.All.ShouldContain(new Country(Countries.NewZealand, "New Zealand"));
        Countries.All.ShouldContain(new Country(Countries.Australia, "Australia"));
        Countries.All.ShouldContain(new Country("US", "United States of America"));
        Countries.All.ShouldContain(new Country("GB", "United Kingdom of Great Britain and Northern Ireland"));
    }

    [Test]
    public void Constants_AreNewZealandAndAustralia()
    {
        Countries.NewZealand.ShouldBe("NZ");
        Countries.Australia.ShouldBe("AU");
    }
}
