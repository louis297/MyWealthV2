using MyWealthV2.Application.TaxYears.Queries.GetCustomerTaxYear;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.TaxYears;

/// <summary>Tax-year-events R4, R30: which tax years can be read, with the platform's clock.</summary>
public class GetCustomerTaxYearQueryValidatorTests
{
    [TestCase("2026-04-02T00:00:00Z", "2026")]
    [TestCase("2026-11-28T03:00:00Z", "2026")]
    [TestCase("2026-11-28T03:00:00Z", "2009")]
    public void AnEndedYearWhose31MarchIsFinal_IsValid(string now, string taxYear) =>
        Validate(now, taxYear).IsValid.ShouldBeTrue();

    [TestCase("2026-04-01T23:59:59Z", "2026")]
    [TestCase("2026-03-31T12:00:00Z", "2026")]
    [TestCase("2026-11-28T03:00:00Z", "2027")]
    public void AYearNotEndedOrNotFinal_IsRefused(string now, string taxYear) =>
        Messages(now, taxYear).ShouldBe([$"Tax year {taxYear} can be read from 2 April {taxYear}."]);

    [TestCase("2008")]
    [TestCase("-5")]
    public void AYearBefore2009_IsRefused(string taxYear) =>
        Messages("2026-11-28T03:00:00Z", taxYear).ShouldBe(["TaxYear must be 2009 or later."]);

    [TestCase("abc")]
    [TestCase("2026.0")]
    [TestCase("")]
    [TestCase(null)]
    [TestCase("99999999999")]
    public void ATaxYearThatIsNotAWholeNumber_IsRefused(string? taxYear) =>
        Messages("2026-11-28T03:00:00Z", taxYear).ShouldBe(["TaxYear must be a whole number."]);

    private static FluentValidation.Results.ValidationResult Validate(string now, string? taxYear) =>
        new GetCustomerTaxYearQueryValidator(new FixedTime(DateTimeOffset.Parse(now))).Validate(new GetCustomerTaxYearQuery(Guid.NewGuid(), taxYear));

    private static List<string> Messages(string now, string? taxYear)
    {
        var result = Validate(now, taxYear);
        result.Errors.ShouldAllBe(error => error.PropertyName == "TaxYear");
        return result.Errors.Select(error => error.ErrorMessage).ToList();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
