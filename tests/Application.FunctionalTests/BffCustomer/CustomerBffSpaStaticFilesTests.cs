using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Hosting;

namespace MyWealthV2.Application.FunctionalTests.BffCustomer;

// bff-customer-portal.md amendment 2026-11-12 (serve the SPA build), P-T1 to P-T7.
// P-T8 (Spa:DevServerUrl set) is DevServerSet_ClientRouteIsForwardedToVite.
public class CustomerBffSpaStaticFilesTests
{
    private TestSpaBuild _build = null!;
    private CustomerBffFactory _factory = null!;
    private HttpClient _bff = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _build = TestSpaBuild.Create();
        _factory = CreateFactory(_build);
        _bff = CreateClient(_factory);
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _bff.Dispose();
        await _factory.DisposeAsync();
        _build.Dispose();
    }

    [TestCase("/")]
    [TestCase("/some/client/route")]
    [TestCase("/accounts/acc_123")]
    public async Task ClientRoute_ReturnsIndexWithNoCache(string url)
    {
        var response = await _bff.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        (await response.Content.ReadAsStringAsync()).ShouldBe(TestSpaBuild.IndexBody);
        response.Headers.CacheControl!.NoCache.ShouldBeTrue();
    }

    [Test]
    public async Task HeadOnClientRoute_ReturnsIndexHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, "/some/client/route");
        var response = await _bff.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        response.Headers.CacheControl!.NoCache.ShouldBeTrue();
    }

    [Test]
    public async Task IndexFile_Direct_HasNoCache()
    {
        var response = await _bff.GetAsync("/index.html");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe(TestSpaBuild.IndexBody);
        response.Headers.CacheControl!.NoCache.ShouldBeTrue();
    }

    [Test]
    public async Task HashedAsset_IsServedImmutable()
    {
        var response = await _bff.GetAsync(TestSpaBuild.AssetPath);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBeOneOf("text/javascript", "application/javascript");
        (await response.Content.ReadAsStringAsync()).ShouldBe(TestSpaBuild.AssetBody);
        var cache = response.Headers.CacheControl!;
        cache.Public.ShouldBeTrue();
        cache.MaxAge.ShouldBe(TimeSpan.FromSeconds(31536000));
        cache.Extensions.ShouldContain(e => e.Name == "immutable");
    }

    [TestCase("/assets/missing.js")]
    [TestCase("/some/client/missing.css")]
    public async Task MissingFileWithExtension_Is404(string url)
    {
        var response = await _bff.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldNotBe(TestSpaBuild.IndexBody);
    }

    [TestCase("/bff/unknown")]
    [TestCase("/signin-oidc/extra")]
    [TestCase("/signout-callback-oidc/extra")]
    [TestCase("/alive")]
    public async Task ReservedPath_Is404NotIndex(string url)
    {
        var response = await _bff.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldNotBe(TestSpaBuild.IndexBody);
    }

    [Test]
    public async Task Health_OnTrafficPort_Is404NotIndex()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation(TestConnection.LocalPortHeader, "8080");
        var response = await _bff.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldNotBe(TestSpaBuild.IndexBody);
    }

    [TestCase("/api", HttpStatusCode.NotFound)]
    [TestCase("/api/users/me", HttpStatusCode.Unauthorized)]
    public async Task Api_WithoutSession_KeepsItsStatus(string url, HttpStatusCode expected)
    {
        var response = await _bff.GetAsync(url);

        response.StatusCode.ShouldBe(expected);
        (await response.Content.ReadAsStringAsync()).ShouldNotBe(TestSpaBuild.IndexBody);
    }

    [Test]
    public async Task PostOnClientRoute_IsNotIndex()
    {
        var response = await _bff.PostAsync("/some/client/route", new StringContent(""));

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await response.Content.ReadAsStringAsync()).ShouldNotBe(TestSpaBuild.IndexBody);
    }

    [TestCase("/some/client/route")]
    [TestCase("/assets/app.js")]
    public async Task SpaResponses_HaveSecurityHeaders(string url)
    {
        var response = await _bff.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        response.Headers.GetValues("Content-Security-Policy").ShouldBe(["frame-ancestors 'none'"]);
    }

    [Test]
    public async Task NoIndex_ClientRouteIs404_AndLoginStillAnswers()
    {
        using var build = TestSpaBuild.Create(withIndex: false);
        await using var factory = CreateFactory(build);
        using var client = CreateClient(factory);

        var route = await client.GetAsync("/some/client/route");
        route.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var login = await client.GetAsync("/bff/login");
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Test]
    public async Task DevServerSet_ClientRouteIsForwardedToVite()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var vite = builder.Build();
        var seen = new List<string>();
        vite.Run(async context =>
        {
            lock (seen)
            {
                seen.Add(context.Request.Path + context.Request.QueryString);
            }

            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("vite-ok");
        });
        await vite.StartAsync();
        var viteAddress = vite.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();

        await using var factory = new CustomerBffFactory(
            "http://127.0.0.1",
            new HttpClientHandler(),
            spaDevServerUrl: viteAddress,
            webRoot: _build.Folder);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/some/client/route");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("vite-ok");
        lock (seen)
        {
            seen.ShouldBe(["/some/client/route"]);
        }
    }

    private static CustomerBffFactory CreateFactory(TestSpaBuild build)
    {
        using var identity = FunctionalTestSetup.Identity.CreateClient();
        return new CustomerBffFactory(
            identity.BaseAddress!.ToString(),
            FunctionalTestSetup.Identity.Server.CreateHandler(),
            environment: "Production",
            webRoot: build.Folder);
    }

    private static HttpClient CreateClient(CustomerBffFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
}
