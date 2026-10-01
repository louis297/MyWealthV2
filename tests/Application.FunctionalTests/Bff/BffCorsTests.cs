using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MyWealthV2.Application.FunctionalTests.Bff;

public class BffCorsTests
{
    [Test]
    public async Task WebApi_DoesNotCredentialThePortalOrigin_AndScalarStays()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/users/me");
        request.Headers.TryAddWithoutValidation("Origin", "https://portal.example");
        var response = await FunctionalTestSetup.WebClient.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();

        var scalar = await FunctionalTestSetup.WebClient.GetAsync("/scalar");
        scalar.StatusCode.ShouldNotBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Bff_DoesNotEnableCredentialedCors()
    {
        using var factory = new BffFactory("http://localhost", new HttpClientHandler());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/bff/login?returnUrl=https://evil.example");
        request.Headers.TryAddWithoutValidation("Origin", "https://portal.example");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
    }
}
