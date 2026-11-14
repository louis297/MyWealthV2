using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using MyWealthV2.Application.FunctionalTests.Infrastructure;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace MyWealthV2.Application.FunctionalTests.Hosting;

// production-hosting R17: requests to /health and /alive produce no ASP.NET Core trace; other requests do.
public class HealthTraceTests
{
    [Test]
    public async Task WebApiProduction_HealthRequests_AreNotTraced_OtherRequestsAre()
    {
        var recorder = new RecordingProcessor();
        using var production = new WebApiFactory(FunctionalTestSetup.ConnectionString, environment: "Production");
        using var factory = production.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(recorder))));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var tracedPath = $"/no-such-route-{Guid.NewGuid():N}";

        await client.SendAsync(OnPort("/health", 8081));
        await client.SendAsync(OnPort("/alive", 8081));
        await client.SendAsync(OnPort(tracedPath, 8080));

        // The server activity ends after the response reaches the client, so wait for the last request's trace.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!recorder.HasServerTrace(tracedPath) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        recorder.HasServerTrace(tracedPath).ShouldBeTrue();
        recorder.HasServerTrace("/health").ShouldBeFalse();
        recorder.HasServerTrace("/alive").ShouldBeFalse();
    }

    private static HttpRequestMessage OnPort(string path, int port)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(TestConnection.LocalPortHeader, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return request;
    }

    private sealed class RecordingProcessor : BaseProcessor<Activity>
    {
        private readonly ConcurrentQueue<string> _serverPaths = new();

        public override void OnEnd(Activity data)
        {
            if (data.Kind == ActivityKind.Server && data.GetTagItem("url.path") is string path)
            {
                _serverPaths.Enqueue(path);
            }
        }

        public bool HasServerTrace(string path) => _serverPaths.Contains(path);
    }
}
