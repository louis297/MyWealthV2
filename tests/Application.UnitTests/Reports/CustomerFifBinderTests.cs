using System.Text.Json;
using MyWealthV2.Application.Reports;
using NUnit.Framework;
using Shouldly;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.UnitTests.Reports;

/// <summary>Fif-report R6, R7: the two parameters, a wrong type, an unknown field, and which tax years can be reported.</summary>
public class CustomerFifBinderTests
{
    private static readonly Guid Customer = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static readonly DateOnly Today = new(2026, 11, 28);

    private static CustomerTaxYearBinding Bind(string json, DateOnly? today = null)
    {
        using var document = JsonDocument.Parse(json);
        return CustomerFifBinder.Bind(document.RootElement, today ?? Today);
    }

    private static void ShouldRefuse(string json, string key, string message, DateOnly? today = null)
    {
        var exception = Should.Throw<ValidationException>(() => Bind(json, today));
        exception.Errors.Keys.ShouldBe([key]);
        exception.Errors[key].ShouldBe([message]);
    }

    [Test]
    public void Bind_TheTwoFields_GivesTheBindingAndASnapshotOfTheTwoFieldsOnly()
    {
        var binding = Bind($$"""{"customerId":"{{Customer}}","taxYear":2026}""");

        binding.CustomerId.ShouldBe(Customer);
        binding.TaxYear.ShouldBe(2026);
        binding.SnapshotJson.ShouldBe($$"""{"customerId":"{{Customer}}","taxYear":2026}""");
        CustomerFifBinder.Code.ShouldBe("customer-fif");
    }

    [Test]
    public void Bind_FieldsInAnotherOrder_GivesTheSameSnapshot()
    {
        var binding = Bind($$"""{"taxYear":2026,"customerId":"{{Customer}}"}""");

        binding.SnapshotJson.ShouldBe($$"""{"customerId":"{{Customer}}","taxYear":2026}""");
    }

    [Test]
    public void Bind_NoCustomerId_IsRequired()
    {
        ShouldRefuse("""{"taxYear":2026}""", "customerId", "customerId is required.");
        ShouldRefuse($$"""{"customerId":"{{Guid.Empty}}","taxYear":2026}""", "customerId", "customerId is required.");
    }

    [Test]
    public void Bind_NoTaxYear_IsRequired()
    {
        ShouldRefuse($$"""{"customerId":"{{Customer}}"}""", "taxYear", "taxYear is required.");
        ShouldRefuse($$"""{"customerId":"{{Customer}}","taxYear":null}""", "taxYear", "taxYear is required.");
    }

    [TestCase("\"2026\"")]
    [TestCase("2026.5")]
    [TestCase("true")]
    [TestCase("[2026]")]
    public void Bind_ATaxYearThatIsNotAWholeNumber_IsNotValid(string taxYear)
    {
        ShouldRefuse(
            $$"""{"customerId":"{{Customer}}","taxYear":{{taxYear}}}""",
            "parameters",
            "parameters are not valid.");
    }

    [Test]
    public void Bind_ACustomerIdThatIsNotAnId_IsNotValid()
    {
        ShouldRefuse("""{"customerId":"nope","taxYear":2026}""", "parameters", "parameters are not valid.");
    }

    [Test]
    public void Bind_AnUnknownField_IsRefused()
    {
        ShouldRefuse(
            $$"""{"customerId":"{{Customer}}","taxYear":2026,"from":"2025-04-01"}""",
            "parameters",
            "unknown field.");
    }

    [Test]
    public void Bind_2008_IsBeforeTheFirstTaxYear()
    {
        ShouldRefuse($$"""{"customerId":"{{Customer}}","taxYear":2008}""", "taxYear", "taxYear must be 2009 or later.");
    }

    [Test]
    public void Bind_2009_CanBeReported()
    {
        Bind($$"""{"customerId":"{{Customer}}","taxYear":2009}""").TaxYear.ShouldBe(2009);
    }

    [Test]
    public void Bind_AYearInProgress_CannotBeReportedYet()
    {
        ShouldRefuse(
            $$"""{"customerId":"{{Customer}}","taxYear":2027}""",
            "taxYear",
            "Tax year 2027 can be reported from 2 April 2027.");
    }

    [Test]
    public void Bind_On1April_TheYearJustEndedIsNotFinal_On2AprilItIs()
    {
        ShouldRefuse(
            $$"""{"customerId":"{{Customer}}","taxYear":2027}""",
            "taxYear",
            "Tax year 2027 can be reported from 2 April 2027.",
            new DateOnly(2027, 4, 1));

        Bind($$"""{"customerId":"{{Customer}}","taxYear":2027}""", new DateOnly(2027, 4, 2)).TaxYear.ShouldBe(2027);
    }
}
