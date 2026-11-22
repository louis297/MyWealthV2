using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Broker;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>Runs the steps of <c>broker-sync:{connection}</c> on the functional host, against its fake broker
/// (orders §10).</summary>
internal static class BrokerHttp
{
    public const string Connection = FakeBrokerConnections.Simulator;

    public static InMemoryBrokerOrders Orders => FunctionalTestSetup.WebApiServices.GetRequiredService<InMemoryBrokerOrders>();

    public static InMemoryBrokerFeed Feed => FunctionalTestSetup.WebApiServices.GetRequiredService<InMemoryBrokerFeed>();

    /// <summary>The pull step (R24).</summary>
    public static async Task<BrokerPullOutcome> PullAsync(string connection = Connection)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PullExecutionReports>().RunAsync(connection);
    }

    /// <summary>The processing step (R25).</summary>
    public static async Task<ReportProcessingOutcome> ProcessAsync(string connection = Connection)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProcessExecutionReports>().RunAsync(connection);
    }

    /// <summary>The pull step, then the processing step.</summary>
    public static async Task<ReportProcessingOutcome> RunReportsAsync(string connection = Connection)
    {
        await PullAsync(connection);
        return await ProcessAsync(connection);
    }

    /// <summary>The stored reports of a connection, in the order they were stored.</summary>
    public static async Task<List<ExecutionReport>> ReportsAsync(string connection = Connection)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ExecutionReports.AsNoTracking()
            .Where(report => report.Connection == connection)
            .OrderBy(report => report.Id)
            .ToListAsync();
    }

    public static async Task<ExecutionReport> ReportAsync(string outsideId, string connection = Connection)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ExecutionReports.AsNoTracking()
            .SingleAsync(report => report.Connection == connection && report.OutsideId == outsideId);
    }

    public static async Task<BrokerFeed?> FeedStateAsync(string connection = Connection)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.BrokerFeeds.AsNoTracking().SingleOrDefaultAsync(feed => feed.Connection == connection);
    }

    /// <summary>The send step (R20, R21).</summary>
    public static async Task<BrokerStepOutcome> SendAsync(string connection = Connection)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SendBrokerOrders>().SendAsync(connection);
    }

    /// <summary>The cancel step (R22).</summary>
    public static async Task<BrokerStepOutcome> CancelAsync(string connection = Connection)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SendBrokerOrders>().CancelAsync(connection);
    }
}
