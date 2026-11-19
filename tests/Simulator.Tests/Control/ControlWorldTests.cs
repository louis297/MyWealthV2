using System.Net;
using System.Text;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests.Control;

/// <summary>Simulator spec R19–R25, R27–R29 and §8: the world, its clock and the control log over HTTP.</summary>
public class ControlWorldTests
{
    private static readonly DateTimeOffset T0 = TestClock.Start;

    private SimulatorFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void StartHost()
    {
        _factory = new SimulatorFactory();
        _client = _factory.SessionClient();
    }

    [OneTimeTearDown]
    public void StopHost()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [SetUp]
    public async Task ResetState()
    {
        _factory.Clock.Set(T0);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, """
            DELETE FROM [ControlLog];
            UPDATE [World] SET [ClockOffsetSeconds] = 0, [PausedAt] = NULL, [PartyOutage] = 0;
            """);
    }

    [Test]
    public async Task GetWorld_ReturnsTheWorldItem()
    {
        var response = await _client.GetAsync("/control/world");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"now\":\"2026-11-19T08:00:00Z\"");
        var world = await response.ReadWorldAsync();
        world.WorldId.ShouldNotBe(Guid.Empty);
        world.Now.ShouldBe(T0);
        world.Paused.ShouldBeFalse();
        world.PartyOutage.ShouldBeFalse();
    }

    [Test]
    public async Task FirstStart_CreatesOneWorld_AndASecondStartKeepsIt()
    {
        var connectionString = await SimulatorTestSetup.CreateScratchDatabaseAsync("world");

        Guid first;
        using (var host = new SimulatorFactory(connectionString))
        using (var client = host.SessionClient())
        {
            first = (await (await client.GetAsync("/control/world")).ReadWorldAsync()).WorldId;
        }

        using (var host = new SimulatorFactory(connectionString))
        using (var client = host.SessionClient())
        {
            (await (await client.GetAsync("/control/world")).ReadWorldAsync()).WorldId.ShouldBe(first);
        }

        (await Sql.ReadAsync(connectionString, "SELECT [WorldId] FROM [World]")).Count.ShouldBe(1);
    }

    [Test]
    public async Task AdvanceBy_MovesTheClock_AndWritesOneLogRow()
    {
        var response = await _client.PostJsonAsync("/control/clock/advance", """{ "by": "P2D" }""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.ReadWorldAsync()).Now.ShouldBe(T0.AddDays(2));
        _factory.Clock.Advance(TimeSpan.FromHours(1));
        (await (await _client.GetAsync("/control/world")).ReadWorldAsync()).Now.ShouldBe(T0.AddDays(2).AddHours(1));

        var log = await LogRowsAsync();
        var row = log.ShouldHaveSingleItem();
        row.Action.ShouldBe("ClockAdvanced");
        row.Actor.ShouldBe(SimulatorFactory.OperatorEmail);
        row.At.ShouldBe(T0);
        row.SimulatorAt.ShouldBe(T0.AddDays(2));
        row.Detail.ShouldContain("\"by\":\"P2D\"");
    }

    [Test]
    public async Task AdvanceTo_MovesTheClockToTheInstant()
    {
        var response = await _client.PostJsonAsync("/control/clock/advance", """{ "to": "2026-11-30T23:00:00Z" }""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.ReadWorldAsync()).Now.ShouldBe(new DateTimeOffset(2026, 11, 30, 23, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task AdvanceWhilePaused_MovesThePausedInstant()
    {
        (await _client.PostJsonAsync("/control/clock/pause", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        _factory.Clock.Advance(TimeSpan.FromHours(4));

        var response = await _client.PostJsonAsync("/control/clock/advance", """{ "by": "PT3H" }""");

        var world = await response.ReadWorldAsync();
        world.Paused.ShouldBeTrue();
        world.Now.ShouldBe(T0.AddHours(3));
    }

    [TestCase("""{ "by": "PT0S" }""", "By")]
    [TestCase("""{ "by": "-P1D" }""", "By")]
    [TestCase("""{ "by": "P367D" }""", "By")]
    [TestCase("""{ "by": "two days" }""", "By")]
    [TestCase("""{ "by": "P1D", "to": "2026-11-30T00:00:00Z" }""", "By")]
    [TestCase("""{}""", "By")]
    [TestCase("""{ "to": "2026-11-19T07:00:00Z" }""", "To")]
    [TestCase("""{ "to": "2026-11-19T08:00:00Z" }""", "To")]
    [TestCase("""{ "to": "2027-11-21T08:00:00Z" }""", "To")]
    public async Task Advance_Refused_Returns400WithTheField_AndWritesNoLogRow(string body, string field)
    {
        var response = await _client.PostJsonAsync("/control/clock/advance", body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorKeysAsync()).ShouldBe([field]);
        (await LogRowsAsync()).ShouldBeEmpty();
        (await (await _client.GetAsync("/control/world")).ReadWorldAsync()).Now.ShouldBe(T0);
    }

    [Test]
    public async Task Pause_FreezesNow_AndResume_ContinuesWithoutAJump()
    {
        await _client.PostJsonAsync("/control/clock/advance", """{ "by": "P1D" }""");
        var paused = await (await _client.PostJsonAsync("/control/clock/pause", "{}")).ReadWorldAsync();
        paused.Paused.ShouldBeTrue();
        paused.Now.ShouldBe(T0.AddDays(1));

        _factory.Clock.Advance(TimeSpan.FromHours(2));
        (await (await _client.GetAsync("/control/world")).ReadWorldAsync()).Now.ShouldBe(T0.AddDays(1));

        var resumed = await (await _client.PostJsonAsync("/control/clock/resume", "{}")).ReadWorldAsync();
        resumed.Paused.ShouldBeFalse();
        resumed.Now.ShouldBe(T0.AddDays(1));
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        (await (await _client.GetAsync("/control/world")).ReadWorldAsync()).Now.ShouldBe(T0.AddDays(1).AddMinutes(1));

        (await LogRowsAsync()).Select(row => row.Action).ShouldBe(["ClockResumed", "ClockPaused", "ClockAdvanced"]);
    }

    [Test]
    public async Task PauseWhilePaused_Returns200AndChangesNothing()
    {
        await _client.PostJsonAsync("/control/clock/pause", "{}");
        _factory.Clock.Advance(TimeSpan.FromHours(1));

        var again = await _client.PostJsonAsync("/control/clock/pause", "{}");

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.ReadWorldAsync()).Now.ShouldBe(T0);
    }

    [Test]
    public async Task Reset_WithASeed_StartsANewWorld_AndKeepsTheLog()
    {
        var before = await (await _client.PostJsonAsync("/control/clock/advance", """{ "by": "P5D" }""")).ReadWorldAsync();
        _factory.Clock.Advance(TimeSpan.FromMinutes(10));

        var response = await _client.PostJsonAsync("/control/reset", """{ "seed": 42 }""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await response.ReadWorldAsync();
        after.WorldId.ShouldNotBe(before.WorldId);
        after.Seed.ShouldBe(42);
        after.Now.ShouldBe(T0.AddMinutes(10));
        after.StartedAt.ShouldBe(T0.AddMinutes(10));
        after.Paused.ShouldBeFalse();
        after.PartyOutage.ShouldBeFalse();
        (await LogRowsAsync()).Select(row => row.Action).ShouldBe(["WorldReset", "ClockAdvanced"]);
    }

    [Test]
    public async Task Reset_WithoutASeed_StartsANewWorld()
    {
        var before = await (await _client.GetAsync("/control/world")).ReadWorldAsync();

        var response = await _client.PostJsonAsync("/control/reset", "{}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.ReadWorldAsync()).WorldId.ShouldNotBe(before.WorldId);
    }

    [Test]
    public async Task Outage_TurnsOnAndOff()
    {
        var on = await _client.PostJsonAsync("/control/party/outage", """{ "on": true }""");
        on.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await on.ReadWorldAsync()).PartyOutage.ShouldBeTrue();

        var off = await _client.PostJsonAsync("/control/party/outage", """{ "on": false }""");
        (await off.ReadWorldAsync()).PartyOutage.ShouldBeFalse();

        (await LogRowsAsync()).Select(row => row.Action).ShouldBe(["PartyOutageChanged", "PartyOutageChanged"]);
    }

    [Test]
    public async Task Outage_WithoutOn_Returns400()
    {
        var response = await _client.PostJsonAsync("/control/party/outage", "{}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorKeysAsync()).ShouldBe(["On"]);
        (await LogRowsAsync()).ShouldBeEmpty();
    }

    [TestCase("/control/clock/advance")]
    [TestCase("/control/clock/pause")]
    [TestCase("/control/clock/resume")]
    [TestCase("/control/party/outage")]
    [TestCase("/control/reset")]
    public async Task Write_WithAnotherContentType_Returns415(string path)
    {
        var response = await _client.PostAsync(path, new StringContent("{}", Encoding.UTF8, "text/plain"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await LogRowsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task Log_ListsNewestFirstWithPaging()
    {
        for (var i = 1; i <= 3; i++)
        {
            await _client.PostJsonAsync("/control/clock/advance", $$"""{ "by": "PT{{i}}H" }""");
            _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var response = await _client.GetAsync("/control/log?page=1&pageSize=2");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = await response.ReadJsonAsync();
        var root = json.RootElement;
        root.GetProperty("page").GetInt32().ShouldBe(1);
        root.GetProperty("pageSize").GetInt32().ShouldBe(2);
        root.GetProperty("totalCount").GetInt32().ShouldBe(3);
        var items = root.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        items[0].GetProperty("detail").GetProperty("by").GetString().ShouldBe("PT3H");
        items[1].GetProperty("detail").GetProperty("by").GetString().ShouldBe("PT2H");
        items[0].GetProperty("actor").GetString().ShouldBe(SimulatorFactory.OperatorEmail);
        items[0].GetProperty("action").GetString().ShouldBe("ClockAdvanced");
        items[0].GetProperty("at").GetDateTimeOffset().ShouldBe(T0.AddMinutes(2));
        items[0].GetProperty("simulatorAt").GetDateTimeOffset().ShouldBe(T0.AddMinutes(2).AddHours(6));

        using var second = await (await _client.GetAsync("/control/log?page=2&pageSize=2")).ReadJsonAsync();
        second.RootElement.GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task Log_DefaultsToPage1Of20_AndCapsThePageSizeAt100()
    {
        using var defaults = await (await _client.GetAsync("/control/log")).ReadJsonAsync();
        defaults.RootElement.GetProperty("page").GetInt32().ShouldBe(1);
        defaults.RootElement.GetProperty("pageSize").GetInt32().ShouldBe(20);

        using var capped = await (await _client.GetAsync("/control/log?pageSize=500")).ReadJsonAsync();
        capped.RootElement.GetProperty("pageSize").GetInt32().ShouldBe(100);
    }

    [Test]
    public async Task ConcurrentWrites_OneWinsAndTheOthersReturn409()
    {
        using var other = _factory.SessionClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            (i % 2 == 0 ? _client : other).PostJsonAsync("/control/clock/advance", """{ "by": "PT1H" }""")));

        responses.ShouldAllBe(response =>
            response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Conflict);
        var succeeded = responses.Count(response => response.StatusCode == HttpStatusCode.OK);
        succeeded.ShouldBeGreaterThan(0);
        (await LogRowsAsync()).Count.ShouldBe(succeeded);
        (await (await _client.GetAsync("/control/world")).ReadWorldAsync()).Now.ShouldBe(T0.AddHours(succeeded));
    }

    private static async Task<List<LogRow>> LogRowsAsync()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT [At], [SimulatorAt], [Actor], [Action], [Detail] FROM [ControlLog] ORDER BY [Id] DESC");
        return rows.Select(row => new LogRow(
            (DateTimeOffset)row[0], (DateTimeOffset)row[1], (string)row[2], (string)row[3], (string)row[4])).ToList();
    }

    private sealed record LogRow(DateTimeOffset At, DateTimeOffset SimulatorAt, string Actor, string Action, string Detail);
}
