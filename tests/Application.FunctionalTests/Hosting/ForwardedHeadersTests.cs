using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using MyWealthV2.Application.FunctionalTests.BackOffice;
using MyWealthV2.Application.FunctionalTests.Bff;
using MyWealthV2.Application.FunctionalTests.BffCustomer;

namespace MyWealthV2.Application.FunctionalTests.Hosting;

// production-hosting R4, R6, R7, R11: forwarded headers from trusted networks only; no HTTPS redirection or HSTS.
public class ForwardedHeadersTests
{
    private const string NetworksKey = "ForwardedHeaders:KnownNetworks:0";

    private TestCertificates _certificates = null!;

    [SetUp]
    public void CreateCertificates() => _certificates = new TestCertificates();

    [TearDown]
    public void Cleanup()
    {
        _certificates.Dispose();
        using var factory = new IdentityFactory(FunctionalTestSetup.ConnectionString);
        _ = factory.Services;
    }

    [Test]
    public async Task Identity_TrustedProxyWithHttpsProto_ServesDiscovery()
    {
        using var factory = ProductionIdentity(ProductionSettings.Identity(_certificates));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(Discovery(ProductionSettings.TrustedProxy));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("authorization_endpoint").GetString()
            .ShouldStartWith("https://", Case.Insensitive);
    }

    [Test]
    public async Task Identity_UntrustedAddressWithHttpsProto_IsRejectedByTransportSecurity()
    {
        using var factory = ProductionIdentity(ProductionSettings.Identity(_certificates));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(Discovery(ProductionSettings.UntrustedAddress));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AdviserBff_TrustedProxyWithHttpsProto_ChallengesWithHttpsRedirectAndSecureCookie()
    {
        using var factory = ProductionAdviserBff();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(Login(ProductionSettings.TrustedProxy));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        RedirectUri(response).ShouldStartWith("https://localhost/signin-oidc");
        var correlation = response.Headers.GetValues("Set-Cookie")
            .Single(cookie => cookie.StartsWith(".AspNetCore.Correlation.", StringComparison.Ordinal));
        correlation.ShouldContain("secure", Case.Insensitive);
    }

    [Test]
    public async Task AdviserBff_UntrustedAddressWithHttpsProto_KeepsRequestScheme()
    {
        using var factory = ProductionAdviserBff();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(Login(ProductionSettings.UntrustedAddress));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        RedirectUri(response).ShouldStartWith("http://localhost/signin-oidc");
    }

    [TestCase(null)]
    [TestCase("not-a-network")]
    [TestCase("10.20.0.0/99")]
    public void Identity_WithoutValidKnownNetworks_FailsStartup(string? network)
    {
        var settings = ProductionSettings.Identity(_certificates);
        SetNetwork(settings, network);
        using var factory = ProductionIdentity(settings);

        StartupFailure.Capture(factory).ShouldContain("ForwardedHeaders:KnownNetworks");
    }

    [TestCase(null)]
    [TestCase("not-a-network")]
    public void AdviserBff_WithoutValidKnownNetworks_FailsStartup(string? network)
    {
        using var factory = new BffFactory("http://127.0.0.1", new SocketsHttpHandler(), environment: "Production");
        SetNetwork(factory.Settings, network);

        StartupFailure.Capture(factory).ShouldContain("ForwardedHeaders:KnownNetworks");
    }

    [TestCase(null)]
    [TestCase("not-a-network")]
    public void CustomerBff_WithoutValidKnownNetworks_FailsStartup(string? network)
    {
        using var factory = new CustomerBffFactory("http://127.0.0.1", new SocketsHttpHandler(), environment: "Production");
        SetNetwork(factory.Settings, network);

        StartupFailure.Capture(factory).ShouldContain("ForwardedHeaders:KnownNetworks");
    }

    [TestCase(null)]
    [TestCase("not-a-network")]
    public void BackOffice_WithoutValidKnownNetworks_FailsStartup(string? network)
    {
        using var factory = new BackOfficeFactory("http://127.0.0.1", new SocketsHttpHandler(), environment: "Production");
        SetNetwork(factory.Settings, network);

        StartupFailure.Capture(factory).ShouldContain("ForwardedHeaders:KnownNetworks");
    }

    [Test]
    public async Task Identity_PlainHttp_IsNotRedirected()
    {
        var settings = ProductionSettings.Identity(_certificates);
        settings["https_port"] = "443";
        using var factory = ProductionIdentity(settings);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/login");

        response.StatusCode.ShouldNotBeOneOf(HttpStatusCode.TemporaryRedirect, HttpStatusCode.PermanentRedirect);
    }

    [Test]
    public async Task WebApi_PlainHttp_IsNotRedirected_AndHttpsHasNoHsts()
    {
        using var factory = new WebApiFactory(
            FunctionalTestSetup.ConnectionString,
            environment: "Production",
            settings: new Dictionary<string, string> { ["https_port"] = "443" });

        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://api.example.test")
        });
        var plain = await http.GetAsync("/");
        plain.StatusCode.ShouldNotBeOneOf(HttpStatusCode.TemporaryRedirect, HttpStatusCode.PermanentRedirect);

        using var https = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://api.example.test")
        });
        var secure = await https.GetAsync("/");
        secure.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    private static IdentityFactory ProductionIdentity(IReadOnlyDictionary<string, string> settings) =>
        new(FunctionalTestSetup.ConnectionString, settings, environment: "Production");

    private static BffFactory ProductionAdviserBff()
    {
        using var identity = FunctionalTestSetup.Identity.CreateClient();
        return new BffFactory(
            identity.BaseAddress!.ToString(),
            FunctionalTestSetup.Identity.Server.CreateHandler(),
            environment: "Production");
    }

    private static HttpRequestMessage Discovery(string remoteAddress) =>
        Forwarded(HttpMethod.Get, "/.well-known/openid-configuration", remoteAddress);

    private static HttpRequestMessage Login(string remoteAddress) =>
        Forwarded(HttpMethod.Get, "/bff/login", remoteAddress);

    private static HttpRequestMessage Forwarded(HttpMethod method, string url, string remoteAddress)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation(TestConnection.RemoteIpHeader, remoteAddress);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.7");
        return request;
    }

    private static string RedirectUri(HttpResponseMessage response) =>
        HttpUtility.ParseQueryString(response.Headers.Location!.Query)["redirect_uri"].ShouldNotBeNull();

    private static void SetNetwork(Dictionary<string, string> settings, string? network)
    {
        if (network is null)
        {
            settings.Remove(NetworksKey);
            return;
        }

        settings[NetworksKey] = network;
    }
}
