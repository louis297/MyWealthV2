using System.Net;
using System.Net.Http.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests.Party;

/// <summary>Simulator spec R27, R30–R33: the party surface behind the machine credential.</summary>
public class PartyStatusTests
{
    private SimulatorFactory _factory = null!;

    [OneTimeSetUp]
    public void StartHost() => _factory = new SimulatorFactory();

    [OneTimeTearDown]
    public void StopHost() => _factory.Dispose();

    [SetUp]
    public async Task ResetState()
    {
        _factory.Clock.Set(TestClock.Start);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, """
            DELETE FROM [ControlLog];
            UPDATE [World] SET [ClockOffsetSeconds] = 0, [PausedAt] = NULL, [PartyOutage] = 0;
            """);
    }

    [Test]
    public async Task Status_WithTheKey_ReturnsTheWorldIdAndSimulatorNow()
    {
        using var control = _factory.SessionClient();
        var world = await (await control.PostJsonAsync("/control/clock/advance", """{ "by": "P2D" }""")).ReadWorldAsync();
        using var party = _factory.PartyClient();

        var response = await party.GetAsync("/party/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<StatusBody>();
        status!.WorldId.ShouldBe(world.WorldId);
        status.Now.ShouldBe(TestClock.Start.AddDays(2));
    }

    [Test]
    public async Task Status_WithoutAKey_Returns401WithNoDetail()
    {
        using var party = _factory.PartyClient(apiKey: null);

        var response = await party.GetAsync("/party/status");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [TestCase("wrong-key")]
    [TestCase(SimulatorFactory.PartyApiKey + "x")]
    [TestCase("TEST-PARTY-API-KEY-0123456789ABCDEF")]
    public async Task Status_WithAWrongKey_Returns401(string key)
    {
        using var party = _factory.PartyClient(apiKey: key);

        var response = await party.GetAsync("/party/status");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task Outage_MakesPartyRoutesAnswer503_AndLeavesControlRoutesAlone()
    {
        using var control = _factory.SessionClient();
        using var party = _factory.PartyClient();
        (await control.PostJsonAsync("/control/party/outage", """{ "on": true }""")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await party.GetAsync("/party/status")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await control.GetAsync("/control/world")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await control.PostJsonAsync("/control/party/outage", """{ "on": false }""")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await party.GetAsync("/party/status")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Outage_ChecksTheKeyFirst()
    {
        using var control = _factory.SessionClient();
        await control.PostJsonAsync("/control/party/outage", """{ "on": true }""");
        using var party = _factory.PartyClient(apiKey: null);

        (await party.GetAsync("/party/status")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("0123456789012345678901234567890")]
    public void Start_WithAMissingOrShortKey_FailsBeforeListening(string? key)
    {
        using var factory = new SimulatorFactory(
            environment: "Production",
            settings: new Dictionary<string, string?> { ["Simulator:PartyApiKey"] = key });

        var failure = Should.Throw<Exception>(() => _ = factory.Server);

        Messages(failure).ShouldContain("Simulator:PartyApiKey");
    }

    [Test]
    public void Start_WithAShortKeyInDevelopment_FailsToo()
    {
        using var factory = new SimulatorFactory(
            settings: new Dictionary<string, string?> { ["Simulator:PartyApiKey"] = "short" });

        Should.Throw<Exception>(() => _ = factory.Server);
    }

    private static string Messages(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private sealed record StatusBody(Guid WorldId, DateTimeOffset Now);
}
