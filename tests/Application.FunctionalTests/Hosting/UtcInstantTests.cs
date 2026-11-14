using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyWealthV2.Application.FunctionalTests.Accounts;

namespace MyWealthV2.Application.FunctionalTests.Hosting;

// fixes/utc-instants.md B1: every instant in a response is UTC with Z.
public partial class UtcInstantTests : TestBase
{
    [Test]
    public async Task CustomerCreated_IsUtcWithZ()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(HttpMethod.Get, $"/users/customers/{customer.PublicId}", tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("created").GetString()!.ShouldMatch(UtcInstant().ToString());
    }

    [Test]
    public async Task NetWorthAsOf_IsUtcWithZ()
    {
        var (_, _, _, tokens) = await AccountHttp.SignInAdviser();

        var response = await AccountHttp.Send(HttpMethod.Get, "/net-worth", tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("asOf").GetString()!.ShouldMatch(UtcInstant().ToString());
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?Z$")]
    private static partial Regex UtcInstant();
}
