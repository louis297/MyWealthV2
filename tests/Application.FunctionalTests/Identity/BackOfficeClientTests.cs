using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MyWealthV2.Application.FunctionalTests.Identity;

public class BackOfficeClientTests : TestBase
{
    private const string Origin = "https://back-office.test";

    private IdentityFactory _identity = null!;

    [OneTimeSetUp]
    public void StartIdentity()
    {
        _identity = new IdentityFactory(FunctionalTestSetup.ConnectionString, new Dictionary<string, string>
        {
            ["Identity:BackOfficeOrigin"] = Origin,
            ["Identity:BackOfficeClientSecret"] = BackOfficeTestSecret.Value
        });
        _ = _identity.Services;
    }

    [OneTimeTearDown]
    public async Task StopIdentity()
    {
        await _identity.DisposeAsync();
        using var restore = new IdentityFactory(FunctionalTestSetup.ConnectionString);
        _ = restore.Services;
    }

    [Test]
    public async Task BackOfficeClient_IsConfidential_AndUsesItsOwnOrigin()
    {
        using var scope = _identity.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        (await manager.FindByClientIdAsync("customer-portal")).ShouldBeNull();

        var application = await manager.FindByClientIdAsync(Services.BackOffice);
        application.ShouldNotBeNull();
        (await manager.ValidateClientSecretAsync(application, BackOfficeTestSecret.Value)).ShouldBeTrue();

        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, application);
        descriptor.ClientType.ShouldBe(ClientTypes.Confidential);
        descriptor.Requirements.ShouldContain(Requirements.Features.ProofKeyForCodeExchange);
        descriptor.RedirectUris.Select(uri => uri.AbsoluteUri).ShouldBe([Origin + "/signin-oidc"]);
        descriptor.PostLogoutRedirectUris.Select(uri => uri.AbsoluteUri).ShouldBe([Origin + "/"]);

        var adviser = await manager.FindByClientIdAsync(Services.AdviserPortal);
        adviser.ShouldNotBeNull();
        var adviserDescriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(adviserDescriptor, adviser);
        adviserDescriptor.RedirectUris.Select(uri => uri.AbsoluteUri).ShouldNotContain(Origin + "/signin-oidc");
        adviserDescriptor.PostLogoutRedirectUris.Select(uri => uri.AbsoluteUri).ShouldNotContain(Origin + "/");
    }

    [Test]
    public async Task TenantAdmin_OnBackOffice_IssuesNoCode()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm B", "firmb");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firmb", "Password1!", tenant.Id);

        using var client = _identity.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var login = await SubmitLoginAsync(client, "tess@firmb", "Password1!", "firmb");

        login.Code.ShouldBeNull();
        FailureCopy(login.Body).ShouldBe("Invalid login.");
    }

    private static async Task<HostedLoginResult> SubmitLoginAsync(
        HttpClient client,
        string email,
        string password,
        string tenantCode)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorize = "/connect/authorize?" + string.Join("&",
        [
            "client_id=" + Uri.EscapeDataString(Services.BackOffice),
            "redirect_uri=" + Uri.EscapeDataString(Origin + "/signin-oidc"),
            "response_type=code",
            "scope=" + Uri.EscapeDataString("openid profile offline_access api"),
            "code_challenge=" + challenge,
            "code_challenge_method=S256",
            "state=" + Base64Url(RandomNumberGenerator.GetBytes(16))
        ]);

        var location = await FollowToLoginAsync(client, authorize);
        var loginPage = await client.GetAsync(location);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        if (loginPage.StatusCode != HttpStatusCode.OK)
        {
            return new HostedLoginResult((int)loginPage.StatusCode, loginHtml, loginPage.Headers.Location, Code: null);
        }

        var token = Regex.Match(loginHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        if (string.IsNullOrEmpty(token))
        {
            token = Regex.Match(loginHtml, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"").Groups[1].Value;
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["TenantCode"] = tenantCode,
            ["__RequestVerificationToken"] = token
        });

        var afterLogin = await client.PostAsync(location, form);
        return await CollectAsync(client, afterLogin);
    }

    private static async Task<string> FollowToLoginAsync(HttpClient client, string url)
    {
        for (var i = 0; i < 8; i++)
        {
            var response = await client.GetAsync(url);
            if ((int)response.StatusCode is < 300 or >= 400)
            {
                return url;
            }

            var next = response.Headers.Location;
            if (next is null)
            {
                return url;
            }

            url = next.IsAbsoluteUri ? next.PathAndQuery : next.ToString();
            if (url.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }
        }

        return url;
    }

    private static async Task<HostedLoginResult> CollectAsync(HttpClient client, HttpResponseMessage response)
    {
        var callback = Origin + "/signin-oidc";
        for (var i = 0; i < 8; i++)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (response.Headers.Location is { } location)
            {
                var target = location.IsAbsoluteUri ? location : new Uri(client.BaseAddress!, location);
                if (target.AbsoluteUri.StartsWith(callback, StringComparison.OrdinalIgnoreCase))
                {
                    return new HostedLoginResult(
                        (int)response.StatusCode,
                        body,
                        target,
                        HttpUtility.ParseQueryString(target.Query)["code"]);
                }

                response.Dispose();
                response = await client.GetAsync(location);
                continue;
            }

            return new HostedLoginResult((int)response.StatusCode, body, Location: null, Code: null);
        }

        return new HostedLoginResult(
            (int)response.StatusCode,
            await response.Content.ReadAsStringAsync(),
            response.Headers.Location,
            Code: null);
    }

    private static string FailureCopy(string html)
    {
        var match = Regex.Match(html, @"<p>([^<]+)</p>");
        return match.Success ? match.Groups[1].Value : html;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
