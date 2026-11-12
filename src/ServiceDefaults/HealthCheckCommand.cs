namespace Microsoft.Extensions.Hosting;

// `--healthcheck`: the process calls its own /health on the health port and exits (production-hosting R15).
// Chiseled images have no shell or curl, so the compose health check runs the app itself.
public static class HealthCheckCommand
{
    public const string Argument = "--healthcheck";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static bool IsRequested(string[] args) => throw new NotImplementedException();

    public static Task<int> RunAsync(string[] args) => throw new NotImplementedException();

    public static Task<int> RunAsync(HttpMessageHandler handler, Uri healthUri, TimeSpan timeout, TextWriter output) =>
        Task.FromResult(-1);
}
