using Hangfire;

namespace MyWealthV2.Infrastructure.Broker;

/// <summary>A run in which a step failed or left work for the next run (docs/features/orders.md R19).</summary>
public sealed class BrokerSyncFailedException(string connection, IReadOnlyList<string> steps)
    : Exception($"Broker sync failed for connection {connection}: {string.Join("; ", steps)}.");

/// <summary>One run of <c>broker-sync:{connection}</c> (orders R19).</summary>
public sealed class BrokerSyncJob
{
    public static string JobId(string connection) => $"broker-sync:{connection}";

    public Task Run(string connection, CancellationToken cancellationToken) => throw new NotImplementedException();
}

/// <summary>Registers one recurring job per configured connection, on queue <c>broker</c> (orders R19).</summary>
public static class BrokerSyncRecurringJob
{
    public const string Queue = "broker";

    public static string Cron(int pollMinutes) => throw new NotImplementedException();

    public static void Register(IRecurringJobManager recurring, JobStorage storage, ConfiguredBrokerConnections connections) =>
        throw new NotImplementedException();
}
