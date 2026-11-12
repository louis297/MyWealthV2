using Microsoft.AspNetCore.Mvc.Testing;

namespace MyWealthV2.Application.FunctionalTests.Hosting;

internal static class StartupFailure
{
    // Starts the factory's host and returns every message in the exception chain.
    public static string Capture<TEntryPoint>(WebApplicationFactory<TEntryPoint> factory)
        where TEntryPoint : class
    {
        var exception = Should.Throw<Exception>(() => _ = factory.Server);
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
            if (current is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(e => e.Message));
            }
        }

        return string.Join(Environment.NewLine, messages);
    }
}
