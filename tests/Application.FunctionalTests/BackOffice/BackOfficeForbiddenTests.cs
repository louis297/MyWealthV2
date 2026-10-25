using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

public class BackOfficeForbiddenTests
{
    private const string SessionCookieName = "__Host-back-office";

    private BackOfficeFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void Start()
    {
        using var identity = FunctionalTestSetup.Identity.CreateClient();
        _factory = new BackOfficeFactory(
            identity.BaseAddress!.ToString(),
            FunctionalTestSetup.Identity.Server.CreateHandler());
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [TestCase("/")]
    [TestCase("/tenants")]
    public async Task OtherRole_IsSentToForbidden(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Cookie", SessionCookie("tenantAdmin"));

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.Location!.AbsolutePath.ShouldBe("/forbidden");
        response.Headers.Location.AbsolutePath.ShouldNotContain("authorize");
    }

    [Test]
    public async Task ForbiddenPage_DoesNotChallengeAgain()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/forbidden");
        request.Headers.TryAddWithoutValidation("Cookie", SessionCookie("tenantAdmin"));

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Location.ShouldBeNull();
        (await response.Content.ReadAsStringAsync()).ShouldContain("Forbidden");
    }

    [Test]
    public async Task SystemAdmin_IsNotSentToForbidden()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("Cookie", SessionCookie("systemAdmin"));

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Location.ShouldBeNull();
    }

    private string SessionCookie(string role)
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var identity = new ClaimsIdentity(
            [
                new Claim("role", role),
                new Claim("name", "Test")
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            "name",
            "role");
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties(),
            CookieAuthenticationDefaults.AuthenticationScheme);
        return SessionCookieName + "=" + options.TicketDataFormat.Protect(ticket);
    }
}
