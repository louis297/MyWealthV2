using MyWealthV2.Shared;

namespace MyWealthV2.TestAppHost;

public class Program
{
    /// <summary>
    /// Fixed SA password for the test SQL Server container. A persistent container keeps the
    /// password it was created with, so a generated value would break the next run.
    /// Test-only: the container holds throwaway data and listens on localhost.
    /// </summary>
    private const string TestSqlPassword = "MyWealthV2-Test-Only-1!";

    public static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var password = builder.AddParameter($"{Services.TestDatabaseServer}-password", TestSqlPassword, secret: true);

        var server = builder.AddSqlServer(Services.TestDatabaseServer, password);

        // On a developer machine keep the container running between test runs, so a run does
        // not pay for SQL Server start-up. CI agents are thrown away, so keep the default
        // session lifetime there. Each test project drops and recreates its own database at
        // start-up, so a persistent container never leaks data or schema between runs.
        if (!IsContinuousIntegration())
        {
            server.WithLifetime(ContainerLifetime.Persistent);
        }

        server.AddDatabase(Services.TestDatabase);
        server.AddDatabase(Services.SchemaTestDatabase);

        builder.Build().Run();
    }

    private static bool IsContinuousIntegration() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD"));
}
