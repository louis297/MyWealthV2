using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Broker;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>Runs the steps of <c>broker-sync:{connection}</c> on the functional host, against its fake broker
/// (orders §10).</summary>
internal static class BrokerHttp
{
    public const string Connection = FakeBrokerConnections.Simulator;

    public static InMemoryBrokerOrders Orders => FunctionalTestSetup.WebApiServices.GetRequiredService<InMemoryBrokerOrders>();

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
