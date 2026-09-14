using MyWealthV2.Shared;

namespace MyWealthV2.TestAppHost;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        builder.AddSqlServer(Services.TestDatabaseServer)
            .AddDatabase(Services.TestDatabase);

        builder.Build().Run();
    }
}