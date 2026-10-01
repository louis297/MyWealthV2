using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MyWealthV2.Application.FunctionalTests.Bff;

public class BffSpaProxyTests
{
    private readonly List<Forwarded> _seen = [];
    private readonly object _gate = new();
    private WebApplication _vite = null!;
    private BffFactory _factory = null!;
    private HttpClient _bff = null!;
    private string _viteAddress = null!;
    private string _viteHost = null!;

    [OneTimeSetUp]
    public async Task Start()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _vite = builder.Build();
        _vite.Run(async context =>
        {
            lock (_gate)
            {
                _seen.Add(new Forwarded(
                    context.Request.Host.Value ?? "",
                    context.Request.Path + context.Request.QueryString,
                    context.Request.Headers.Accept.ToString()));
            }

            if (context.Request.Path == "/src/missing.tsx")
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("missing");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/javascript";
            await context.Response.WriteAsync("vite-ok");
        });
        await _vite.StartAsync();

        _viteAddress = _vite.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        _viteHost = new Uri(_viteAddress).Authority;
        _factory = new BffFactory(
            "http://127.0.0.1",
            new HttpClientHandler(),
            spaDevServerUrl: _viteAddress);
        _bff = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _bff.Dispose();
        await _factory.DisposeAsync();
        await _vite.DisposeAsync();
    }

    [TestCase("/src/main.tsx?t=1710000000000")]
    [TestCase("/node_modules/vite/dist/client/env.mjs")]
    [TestCase("/@vite/client")]
    [TestCase("/@fs/tmp/src/main.tsx")]
    [TestCase("/@id/__x00__plugin")]
    [TestCase("/@react-refresh")]
    public async Task DevModule_IsForwardedWithQueryAcceptAndViteHost(string url)
    {
        var result = await SendAsync(url, "*/*");

        result.Status.ShouldBe(HttpStatusCode.OK);
        result.Body.ShouldBe("vite-ok");
        result.ContentType.ShouldBe("text/javascript");
        var hit = result.Forwarded.ShouldHaveSingleItem();
        hit.Target.ShouldBe(url);
        hit.Accept.ShouldBe("*/*");
        hit.Host.ShouldBe(_viteHost);
    }

    [Test]
    public async Task SpaShell_ForwardsAcceptAndViteHost()
    {
        var result = await SendAsync("/", "text/html");

        result.Status.ShouldBe(HttpStatusCode.OK);
        result.Body.ShouldBe("vite-ok");
        var hit = result.Forwarded.ShouldHaveSingleItem();
        hit.Target.ShouldBe("/");
        hit.Accept.ShouldBe("text/html");
        hit.Host.ShouldBe(_viteHost);
    }

    [Test]
    public async Task MissingModule_CopiesViteStatus()
    {
        var result = await SendAsync("/src/missing.tsx", "*/*");

        result.Status.ShouldBe(HttpStatusCode.NotFound);
        result.Body.ShouldBe("missing");
        result.Forwarded.ShouldHaveSingleItem().Target.ShouldBe("/src/missing.tsx");
    }

    [TestCase("/bff/nope")]
    [TestCase("/api/not-a-resource")]
    [TestCase("/health")]
    [TestCase("/alive")]
    public async Task ReservedPath_IsNotForwardedToVite(string url)
    {
        var result = await SendAsync(url, "*/*");

        result.Body.ShouldNotBe("vite-ok");
        result.Forwarded.ShouldBeEmpty();
    }

    [Test]
    public async Task ProductionHealthAndAlive_Stay404()
    {
        using var factory = new BffFactory(
            "http://127.0.0.1",
            new SocketsHttpHandler(),
            environment: "Production",
            spaDevServerUrl: _viteAddress);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        foreach (var url in new[]
                 {
                     "/health",
                     "/alive",
                     "/bff/nope",
                     "/signin-oidc/extra",
                     "/signout-callback-oidc/extra"
                 })
        {
            var before = Count();
            var response = await client.GetAsync(url);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).ShouldNotBe("vite-ok");
            Count().ShouldBe(before);
        }
    }

    private async Task<Proxied> SendAsync(string url, string accept)
    {
        var before = Count();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation("Host", "bff-adviser-portal-mywealthv2.dev.localhost");
        var response = await _bff.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return new Proxied(
            response.StatusCode,
            body,
            response.Content.Headers.ContentType?.ToString(),
            Snapshot(before));
    }

    private int Count()
    {
        lock (_gate)
        {
            return _seen.Count;
        }
    }

    private Forwarded[] Snapshot(int before)
    {
        lock (_gate)
        {
            return _seen.Skip(before).ToArray();
        }
    }

    private sealed record Forwarded(string Host, string Target, string Accept);

    private sealed record Proxied(HttpStatusCode Status, string Body, string? ContentType, Forwarded[] Forwarded);
}
