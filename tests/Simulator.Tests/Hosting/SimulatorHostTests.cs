using System.Net;

namespace MyWealthV2.Simulator.Tests.Hosting;

/// <summary>Simulator spec R11–R15: own scripts on its own database, and routes by port.</summary>
public class SimulatorHostTests
{
    [Test]
    public async Task Start_OnAnEmptyDatabase_AppliesTheSimulatorScripts_AndASecondStartAppliesNothing()
    {
        var connectionString = await SimulatorTestSetup.CreateScratchDatabaseAsync("scripts");

        using (var first = new SimulatorFactory(connectionString))
        {
            _ = first.Server;
        }

        var applied = await Sql.ReadAsync(connectionString,
            "IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NOT NULL SELECT [ScriptName], [AppliedAt] FROM [SchemaVersions] ORDER BY [ScriptName]");
        applied.Select(row => (string)row[0]).ShouldBe(["0001_schema_versions.sql", "0002_world.sql", "0003_bank.sql", "0004_bank_payments.sql", "0005_bank_returns.sql"]);
        (await Sql.ReadAsync(connectionString,
                "SELECT [name] FROM sys.tables WHERE [name] IN (N'World', N'ControlLog') ORDER BY [name]"))
            .Select(row => (string)row[0]).ShouldBe(["ControlLog", "World"]);

        using (var second = new SimulatorFactory(connectionString))
        {
            _ = second.Server;
        }

        var again = await Sql.ReadAsync(connectionString,
            "SELECT [ScriptName], [AppliedAt] FROM [SchemaVersions] ORDER BY [ScriptName]");
        again.Select(row => ((string)row[0], (DateTimeOffset)row[1]))
            .ShouldBe(applied.Select(row => ((string)row[0], (DateTimeOffset)row[1])));
    }

    [Test]
    public async Task PartyRoute_OnTheControlPort_Returns404()
    {
        using var factory = new SimulatorFactory();
        using var client = factory.ControlClient();

        var response = await client.GetAsync("/party/status");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ControlRoute_OnThePartyPort_Returns404()
    {
        using var factory = new SimulatorFactory();
        using var client = factory.PartyClient();

        var response = await client.GetAsync("/control/world");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ControlRoute_OnTheHealthPort_OutsideDevelopment_Returns404()
    {
        using var factory = new SimulatorFactory(environment: "Production");
        using var client = factory.ClientOn(SimulatorFactory.HealthPort);

        var response = await client.GetAsync("/control/world");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task EachSurface_AnswersOnItsOwnPort()
    {
        using var factory = new SimulatorFactory();
        using var control = factory.ControlClient();
        using var party = factory.PartyClient();

        (await control.GetAsync("/control/world")).StatusCode.ShouldNotBe(HttpStatusCode.NotFound);
        (await party.GetAsync("/party/status")).StatusCode.ShouldNotBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Health_OutsideDevelopment_AnswersOnlyOnTheHealthPort()
    {
        using var factory = new SimulatorFactory(environment: "Production");
        using var health = factory.ClientOn(SimulatorFactory.HealthPort);
        using var control = factory.ControlClient();

        (await health.GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await control.GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
