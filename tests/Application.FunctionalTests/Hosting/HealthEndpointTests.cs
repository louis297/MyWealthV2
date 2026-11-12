using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using MyWealthV2.Application.FunctionalTests.BackOffice;

namespace MyWealthV2.Application.FunctionalTests.Hosting;

// production-hosting R12–R14: outside Development, /health and /alive answer only on the health port.
public class HealthEndpointTests
{
    [TestCase("/health")]
    [TestCase("/alive")]
    public async Task WebApiProduction_OnHealthPort_IsHealthy(string path)
    {
        using var factory = ProductionWebApi();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(OnPort(path, 8081));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [TestCase("/health")]
    [TestCase("/alive")]
    public async Task WebApiProduction_OnTrafficPort_IsNotTheHealthResponse(string path)
    {
        using var factory = ProductionWebApi();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(OnPort(path, 8080));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldNotBe("Healthy");
    }

    [Test]
    public async Task WebApiProduction_ConfiguredHealthPort_IsUsed()
    {
        using var factory = ProductionWebApi(new Dictionary<string, string> { ["Health:Port"] = "9091" });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        (await client.SendAsync(OnPort("/health", 9091))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.SendAsync(OnPort("/health", 8081))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task WebApiDevelopment_OnDefaultPort_IsHealthy()
    {
        using var client = new HttpClient(FunctionalTestSetup.WebApiHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("http://localhost")
        };

        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Test]
    public async Task BackOfficeProduction_OnHealthPort_IsHealthy_AndOnTrafficPortFallsThrough()
    {
        using var identity = FunctionalTestSetup.Identity.CreateClient();
        using var factory = new BackOfficeFactory(
            identity.BaseAddress!.ToString(),
            FunctionalTestSetup.Identity.Server.CreateHandler(),
            environment: "Production");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var health = await client.SendAsync(OnPort("/health", 8081));
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await health.Content.ReadAsStringAsync()).ShouldBe("Healthy");

        var traffic = await client.SendAsync(OnPort("/health", 8080));
        traffic.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await traffic.Content.ReadAsStringAsync()).ShouldNotBe("Healthy");
    }

    private static WebApiFactory ProductionWebApi(IReadOnlyDictionary<string, string>? settings = null) =>
        new(FunctionalTestSetup.ConnectionString, environment: "Production", settings: settings);

    private static HttpRequestMessage OnPort(string path, int port)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(TestConnection.LocalPortHeader, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return request;
    }
}
