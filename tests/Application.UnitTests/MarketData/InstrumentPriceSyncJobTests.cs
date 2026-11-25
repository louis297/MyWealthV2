using Microsoft.Extensions.Logging.Abstractions;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.MarketData;

/// <summary>Market data history R7–R11: the daily EODHD job stores every bar from the window's first date.</summary>
public class InstrumentPriceSyncJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Floor = new(2026, 1, 1);

    [Test]
    public async Task WithAFloorDate_AsksFromTheWindowStart_AndStoresEveryBarOnOrAfterIt()
    {
        var source = new FakeSource();
        source.Answer("AAPL", "USD", Bar(2025, 12, 31, 271.86m), Bar(2026, 1, 2, 271.01m), Bar(2026, 1, 5, 267.26m), Bar(2026, 11, 24, 280m));
        var store = new FakeStore();
        var job = Job(source, store, new MarketDataHistorySettings(Floor), new ActiveInstrument(1, "AAPL", "USD"));

        await job.Sync();

        source.Calls.ShouldBe([("AAPL", "USD", (DateOnly?)Floor)]);
        store.Rows.ShouldBe([
            new InstrumentPriceQuote(1, 271.01m, new DateOnly(2026, 1, 2)),
            new InstrumentPriceQuote(1, 267.26m, new DateOnly(2026, 1, 5)),
            new InstrumentPriceQuote(1, 280m, new DateOnly(2026, 11, 24))
        ], ignoreOrder: true);
        store.FetchedAt.ShouldAllBe(fetchedAt => fetchedAt == Now);
    }

    [Test]
    public async Task WithAnOldFloorDate_AsksFromTodayMinusTheHistoryDays()
    {
        var source = new FakeSource();
        var from = new DateOnly(2026, 11, 25).AddDays(-360);
        source.Answer("AAPL", "USD", Bar(from.AddDays(-1), 1m), Bar(from, 2m));
        var store = new FakeStore();
        var job = Job(source, store, new MarketDataHistorySettings(new DateOnly(2020, 1, 1), 360), new ActiveInstrument(1, "AAPL", "USD"));

        await job.Sync();

        source.Calls.ShouldBe([("AAPL", "USD", (DateOnly?)from)]);
        store.Rows.ShouldBe([new InstrumentPriceQuote(1, 2m, from)]);
    }

    [Test]
    public async Task ABarWithNoCloseOrACloseThatIsNotAboveZero_IsSkipped()
    {
        var source = new FakeSource();
        source.Answer("AAPL", "USD", Bar(2026, 1, 2, null), Bar(2026, 1, 5, 0m), Bar(2026, 1, 6, -1m), Bar(2026, 1, 7, 260m));
        var store = new FakeStore();
        var job = Job(source, store, new MarketDataHistorySettings(Floor), new ActiveInstrument(1, "AAPL", "USD"));

        await job.Sync();

        store.Rows.ShouldBe([new InstrumentPriceQuote(1, 260m, new DateOnly(2026, 1, 7))]);
    }

    [Test]
    public async Task WithoutAFloorDate_AsksForTheLatestBar()
    {
        var source = new FakeSource();
        source.Answer("AAPL", "USD", Bar(2026, 11, 24, 280m));
        var store = new FakeStore();
        var job = Job(source, store, MarketDataHistorySettings.None, new ActiveInstrument(1, "AAPL", "USD"));

        await job.Sync();

        source.Calls.ShouldBe([("AAPL", "USD", (DateOnly?)null)]);
        store.Rows.ShouldBe([new InstrumentPriceQuote(1, 280m, new DateOnly(2026, 11, 24))]);
    }

    [Test]
    public async Task AServedInstrument_IsNeverAsked()
    {
        var source = new FakeSource();
        source.Answer("AAPL", "USD", Bar(2026, 1, 2, 271.01m));
        var store = new FakeStore();
        var connections = new MarketDataConnections([new MarketDataConnection("simulator", "Simulator", ".SIM")]);
        var job = Job(
            source,
            store,
            new MarketDataHistorySettings(Floor),
            connections,
            new ActiveInstrument(1, "AAPL", "USD"),
            new ActiveInstrument(2, "ACME.SIM", "USD"));

        await job.Sync();

        source.Calls.ShouldBe([("AAPL", "USD", (DateOnly?)Floor)]);
        store.Rows.ShouldAllBe(row => row.InstrumentId == 1);
    }

    [Test]
    public async Task TheSameSymbolInTwoTenants_GetsEveryBarInBoth_FromOneCall()
    {
        var source = new FakeSource();
        source.Answer("VTI", "USD", Bar(2026, 1, 2, 300m), Bar(2026, 1, 5, 301m));
        var store = new FakeStore();
        var job = Job(
            source,
            store,
            new MarketDataHistorySettings(Floor),
            new ActiveInstrument(1, "VTI", "USD"),
            new ActiveInstrument(2, "vti", "usd"));

        await job.Sync();

        source.Calls.ShouldBe([("VTI", "USD", (DateOnly?)Floor)]);
        store.Rows.ShouldBe([
            new InstrumentPriceQuote(1, 300m, new DateOnly(2026, 1, 2)),
            new InstrumentPriceQuote(1, 301m, new DateOnly(2026, 1, 5)),
            new InstrumentPriceQuote(2, 300m, new DateOnly(2026, 1, 2)),
            new InstrumentPriceQuote(2, 301m, new DateOnly(2026, 1, 5))
        ], ignoreOrder: true);
    }

    [Test]
    public async Task EachSymbolGroupIsStoredOnItsOwn_AFailureLaterKeepsTheGroupsAlreadyStored()
    {
        var source = new FakeSource();
        source.Answer("AAPL", "USD", Bar(2026, 1, 2, 271.01m));
        source.Fail("VTI");
        var store = new FakeStore();
        var job = Job(
            source,
            store,
            new MarketDataHistorySettings(Floor),
            new ActiveInstrument(1, "AAPL", "USD"),
            new ActiveInstrument(2, "VTI", "USD"));

        await job.Sync();

        store.Batches.Count.ShouldBe(1);
        store.Rows.ShouldBe([new InstrumentPriceQuote(1, 271.01m, new DateOnly(2026, 1, 2))]);
    }

    [Test]
    public async Task TwoSymbols_AreStoredInOneBatchEach()
    {
        var source = new FakeSource();
        source.Answer("AAPL", "USD", Bar(2026, 1, 2, 271.01m));
        source.Answer("VTI", "USD", Bar(2026, 1, 2, 300m));
        var store = new FakeStore();
        var job = Job(
            source,
            store,
            new MarketDataHistorySettings(Floor),
            new ActiveInstrument(1, "AAPL", "USD"),
            new ActiveInstrument(2, "VTI", "USD"));

        await job.Sync();

        store.Batches.Select(batch => string.Join(",", batch.Select(row => row.InstrumentId).Distinct())).ShouldBe(["1", "2"]);
    }

    private static SourcedPriceBar Bar(int year, int month, int day, decimal? close) => new(new DateOnly(year, month, day), close);

    private static SourcedPriceBar Bar(DateOnly date, decimal? close) => new(date, close);

    private static InstrumentPriceSyncJob Job(
        IInstrumentPriceSource source,
        IInstrumentPriceStore store,
        MarketDataHistorySettings history,
        params ActiveInstrument[] instruments) =>
        Job(source, store, history, MarketDataConnections.None, instruments);

    private static InstrumentPriceSyncJob Job(
        IInstrumentPriceSource source,
        IInstrumentPriceStore store,
        MarketDataHistorySettings history,
        MarketDataConnections connections,
        params ActiveInstrument[] instruments) =>
        new(
            new FixedCatalog(instruments),
            source,
            store,
            connections,
            history,
            new FixedTime(Now),
            TimeSpan.Zero,
            NullLogger<InstrumentPriceSyncJob>.Instance);

    private sealed class FixedCatalog(ActiveInstrument[] instruments) : IActiveInstrumentCatalog
    {
        public Task<IReadOnlyList<ActiveInstrument>> ListActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActiveInstrument>>(instruments);
    }

    private sealed class FakeSource : IInstrumentPriceSource
    {
        private readonly Dictionary<string, SourcedInstrumentBars> _answers = new(StringComparer.Ordinal);
        private readonly HashSet<string> _failures = new(StringComparer.Ordinal);

        public List<(string Symbol, string Quote, DateOnly? From)> Calls { get; } = [];

        public void Answer(string symbol, string currency, params SourcedPriceBar[] bars) =>
            _answers[symbol] = new SourcedInstrumentBars(currency, bars);

        public void Fail(string symbol) => _failures.Add(symbol);

        public Task<SourcedInstrumentBars?> GetBarsAsync(
            string symbol,
            string quoteCurrency,
            DateOnly? from = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((symbol, quoteCurrency, from));
            if (_failures.Contains(symbol))
            {
                throw new HttpRequestException("EODHD is down.");
            }

            return Task.FromResult(_answers.GetValueOrDefault(symbol));
        }
    }

    private sealed class FakeStore : IInstrumentPriceStore
    {
        public List<IReadOnlyList<InstrumentPriceQuote>> Batches { get; } = [];

        public List<DateTimeOffset> FetchedAt { get; } = [];

        public IEnumerable<InstrumentPriceQuote> Rows => Batches.SelectMany(batch => batch);

        public Task UpsertAsync(IReadOnlyList<InstrumentPriceQuote> quotes, DateTimeOffset fetchedAt, CancellationToken cancellationToken = default)
        {
            Batches.Add(quotes.ToList());
            FetchedAt.Add(fetchedAt);
            return Task.CompletedTask;
        }

        public Task UpsertAsync(IReadOnlyList<InstrumentPriceQuote> quotes, DateTimeOffset fetchedAt, string source, CancellationToken cancellationToken = default) =>
            UpsertAsync(quotes, fetchedAt, cancellationToken);

        public Task<bool> HasAsOfDateForEveryActiveInstrumentAsync(IReadOnlyList<int> instrumentIds, DateOnly asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> HasPriceFromAsync(int instrumentId, DateOnly from, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<DateOnly?> LatestDateAsync(int instrumentId, DateOnly onOrBefore, CancellationToken cancellationToken = default) =>
            Task.FromResult<DateOnly?>(null);
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
