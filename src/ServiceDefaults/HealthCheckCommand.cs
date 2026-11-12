using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.Hosting;

// `--healthcheck`: the process calls its own /health on the health port and exits (production-hosting R15).
// Chiseled images have no shell or curl, so the compose health check runs the app itself.
public static class HealthCheckCommand
{
    public const string Argument = "--healthcheck";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static bool IsRequested(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Argument, StringComparison.Ordinal);

    // Reads Health:Port through appsettings, environment variables and the remaining arguments.
    // Does not build the web host, read the database or run the schema applicator.
    public static async Task<int> RunAsync(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environments.Production;
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args.Skip(1).ToArray())
            .Build();

        var port = Extensions.ReadHealthPort(configuration);
        using var handler = new SocketsHttpHandler();
        return await RunAsync(handler, new Uri($"http://localhost:{port}/health"), Timeout, Console.Out);
    }

    public static async Task<int> RunAsync(HttpMessageHandler handler, Uri healthUri, TimeSpan timeout, TextWriter output)
    {
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = timeout };
        try
        {
            using var response = await client.GetAsync(healthUri);
            var status = (int)response.StatusCode;
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                await output.WriteLineAsync($"Healthy: {healthUri} returned {status}.");
                return 0;
            }

            await output.WriteLineAsync($"Unhealthy: {healthUri} returned {status}.");
            return 1;
        }
        catch (TaskCanceledException)
        {
            await output.WriteLineAsync($"Unhealthy: {healthUri} did not answer within {timeout.TotalSeconds:0.###} s.");
            return 1;
        }
        catch (HttpRequestException exception)
        {
            await output.WriteLineAsync($"Unhealthy: {healthUri} could not be reached ({exception.Message}).");
            return 1;
        }
    }
}
