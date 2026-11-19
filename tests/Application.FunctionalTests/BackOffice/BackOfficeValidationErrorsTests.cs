extern alias BackOfficeHost;

using BackOfficeHost::MyWealthV2.BackOffice;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

/// <summary>BO16: a 400 from the platform API is shown at its field.</summary>
public class BackOfficeValidationErrorsTests
{
    [Test]
    public void BadRequestWithErrors_GivesTheFirstMessagePerField_IgnoringCase()
    {
        var exception = new PlatformApiException(400, """
            {"title":"One or more validation errors occurred.","status":400,
             "errors":{"Currency":["The tenant already has an active client money account in USD.","second"],
                       "AccountNumber":["This account number is already in use by an active client money account."]}}
            """);

        var errors = ValidationErrors.From(exception);

        errors["currency"].ShouldBe("The tenant already has an active client money account in USD.");
        errors["accountNumber"].ShouldBe("This account number is already in use by an active client money account.");
        errors.Count.ShouldBe(2);
    }

    [TestCase(400, """{"title":"Tenant is disabled","code":"disabled","target":"tenant"}""")]
    [TestCase(409, """{"title":"Stale","code":"stale_row_version","errors":{"RowVersion":["stale"]}}""")]
    [TestCase(400, "not json")]
    [TestCase(400, "")]
    public void AnythingElse_GivesNoFieldErrors(int status, string body) =>
        ValidationErrors.From(new PlatformApiException(status, body)).ShouldBeEmpty();
}
