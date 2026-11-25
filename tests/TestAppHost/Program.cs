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

    /// <summary>
    /// Opt-in for a developer machine: the name of a local SQL Server image built from
    /// <c>tests/TestAppHost/sql-tmpfs</c>. When set, the test server keeps its files on tmpfs, in memory,
    /// so a test run does not write its databases to disk. See <c>tests/TestAppHost/sql-tmpfs/README.md</c>.
    /// </summary>
    private const string TmpfsImageVariable = "MYWEALTH_TEST_SQL_TMPFS_IMAGE";

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

            var tmpfsImage = Environment.GetEnvironmentVariable(TmpfsImageVariable);
            if (!string.IsNullOrWhiteSpace(tmpfsImage))
            {
                UseTmpfsImage(server, tmpfsImage);
            }
        }

        server.AddDatabase(Services.TestDatabase);
        server.AddDatabase(Services.SchemaTestDatabase);
        server.AddDatabase(Services.TestSimulatorDatabase);

        builder.Build().Run();
    }

    /// <summary>
    /// Runs the test server from a local image whose files live on tmpfs. The data goes when the
    /// container stops; SQL Server sets up its system databases again when it starts, and each test
    /// project recreates its own databases anyway.
    /// </summary>
    private static void UseTmpfsImage(IResourceBuilder<SqlServerServerResource> server, string image)
    {
        var separator = image.LastIndexOf(':');
        var (name, tag) = separator > 0 ? (image[..separator], image[(separator + 1)..]) : (image, "latest");

        server
            .WithImageRegistry(null)
            .WithImage(name, tag)
            .WithImagePullPolicy(ImagePullPolicy.Never)
            .WithContainerRuntimeArgs("--tmpfs", "/var/opt/mssql:rw,size=16g,uid=10001,gid=10001,mode=0755");
    }

    private static bool IsContinuousIntegration() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD"));
}
