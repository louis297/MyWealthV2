using Microsoft.Extensions.Logging.Abstractions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Fx;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Entities;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Fx;

/// <summary>Market data history R12–R15: one range call per active base, from the floor date to yesterday.</summary>
public class FxRateHistorySyncJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 11, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Floor = new(2026, 1, 1);
    private static readonly DateOnly Yesterday = new(2026, 11, 24);

    [Test]
    public async Task OneRangeCallPerActiveBase_FromTheFloorDateToYesterday_AndOneWritePerBase()
    {
        var source = new FakeSource();
        var store = new FakeStore();
        var job = Job(source, store, new MarketDataHistorySettings(Floor), "USD", "NZD", "AUD");

        await job.Sync();

        source.Calls.Select(call => $"{call.Base}:{string.Join(",", call.Quotes)}:{call.From:yyyy-MM-dd}:{call.To:yyyy-MM-dd}")
            .ShouldBe([
                "USD:NZD,AUD:2026-01-01:2026-11-24",
                "NZD:USD,AUD:2026-01-01:2026-11-24",
                "AUD:USD,NZD:2026-01-01:2026-11-24"
            ]);
        store.Batches.Count.ShouldBe(3);
        store.Batches.Select(batch => batch.Select(row => row.BaseCurrency).Distinct().Single()).ShouldBe(["USD", "NZD", "AUD"]);
        store.FetchedAt.ShouldAllBe(fetchedAt => fetchedAt == Now);
        store.Rows.Count().ShouldBe(6);
    }

    [Test]
    public async Task TheRecordsOfR14_AreSkipped()
    {
        var source = new FakeSource();
        source.Answer("USD", [
            new FxRateQuote("USD", "NZD", 1.7294m, Floor),
            new FxRateQuote("USD", "NZD", 1.7000m, Floor.AddDays(-1)),
            new FxRateQuote("USD", "NZD", 1.7400m, Yesterday.AddDays(1)),
            new FxRateQuote("USD", "USD", 1m, Floor),
            new FxRateQuote("USD", "CHF", 0.9m, Floor),
            new FxRateQuote("USD", "NZD", 0m, Floor.AddDays(1)),
            new FxRateQuote("USD", "NZD", -1m, Floor.AddDays(2)),
            new FxRateQuote("USD", "NZD", 1.7350m, Yesterday)
        ]);
        source.Answer("NZD", []);
        var store = new FakeStore();
        var job = Job(source, store, new MarketDataHistorySettings(Floor), "USD", "NZD");

        await job.Sync();

        store.Rows.ShouldBe([
            new FxRateQuote("USD", "NZD", 1.7294m, Floor),
            new FxRateQuote("USD", "NZD", 1.7350m, Yesterday)
        ], ignoreOrder: true);
    }

    [Test]
    public async Task AFailedBase_StoresNothing_TheOthersAreStored_AndTheRunIsReportedAsFailed()
    {
        var source = new FakeSource();
        source.Fail("NZD");
        var store = new FakeStore();
        var job = Job(source, store, new MarketDataHistorySettings(Floor), "USD", "NZD", "AUD");

        var failure = await Should.ThrowAsync<FxRateHistorySyncFailedException>(() => job.Sync());

        failure.Bases.ShouldBe(["NZD"]);
        source.Calls.Select(call => call.Base).ShouldBe(["USD", "NZD", "AUD"]);
        store.Batches.Select(batch => batch.Select(row => row.BaseCurrency).Distinct().Single()).ShouldBe(["USD", "AUD"]);
    }

    [Test]
    public async Task NoFloorDate_MakesNoCall()
    {
        var source = new FakeSource();
        var store = new FakeStore();
        var job = Job(source, store, MarketDataHistorySettings.None, "USD", "NZD");

        await job.Sync();

        source.Calls.ShouldBeEmpty();
        store.Batches.ShouldBeEmpty();
    }

    [Test]
    public async Task AFloorDateOfToday_MakesNoCall()
    {
        var source = new FakeSource();
        var store = new FakeStore();
        var job = Job(source, store, new MarketDataHistorySettings(new DateOnly(2026, 11, 25)), "USD", "NZD");

        await job.Sync();

        source.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task AFloorDateOfYesterday_AsksForYesterdayOnly()
    {
        var source = new FakeSource();
        var store = new FakeStore();
        var job = Job(source, store, new MarketDataHistorySettings(Yesterday), "USD", "NZD");

        await job.Sync();

        source.Calls.Select(call => (call.From, call.To)).ShouldBe([(Yesterday, Yesterday), (Yesterday, Yesterday)]);
    }

    private static FxRateHistorySyncJob Job(
        IFxRateSource source,
        IFxRateStore store,
        MarketDataHistorySettings history,
        params string[] codes) =>
        new(new FixedCatalog(codes), source, store, history, new FixedTime(Now), NullLogger<FxRateHistorySyncJob>.Instance);

    private sealed class FakeSource : IFxRateSource
    {
        private readonly Dictionary<string, IReadOnlyList<FxRateQuote>> _answers = new(StringComparer.Ordinal);
        private readonly HashSet<string> _failures = new(StringComparer.Ordinal);

        public List<(string Base, IReadOnlyList<string> Quotes, DateOnly From, DateOnly To)> Calls { get; } = [];

        public void Answer(string baseCurrency, IReadOnlyList<FxRateQuote> rows) => _answers[baseCurrency] = rows;

        public void Fail(string baseCurrency) => _failures.Add(baseCurrency);

        public Task<IReadOnlyList<FxRateQuote>> GetLatestAsync(
            string baseCurrency,
            IReadOnlyList<string> quotes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The history job asks for a range.");

        public Task<IReadOnlyList<FxRateQuote>> GetRangeAsync(
            string baseCurrency,
            IReadOnlyList<string> quotes,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((baseCurrency, quotes.ToList(), from, to));
            if (_failures.Contains(baseCurrency))
            {
                throw new HttpRequestException("Frankfurter is down.");
            }

            if (_answers.TryGetValue(baseCurrency, out var rows))
            {
                return Task.FromResult(rows);
            }

            IReadOnlyList<FxRateQuote> defaults = quotes.Select(quote => new FxRateQuote(baseCurrency, quote, 1.5m, from)).ToList();
            return Task.FromResult(defaults);
        }
    }

    private sealed class FakeStore : IFxRateStore
    {
        public List<IReadOnlyList<FxRateQuote>> Batches { get; } = [];

        public List<DateTimeOffset> FetchedAt { get; } = [];

        public IEnumerable<FxRateQuote> Rows => Batches.SelectMany(batch => batch);

        public Task UpsertAsync(IReadOnlyList<FxRateQuote> quotes, DateTimeOffset fetchedAt, CancellationToken cancellationToken = default)
        {
            Batches.Add(quotes.ToList());
            FetchedAt.Add(fetchedAt);
            return Task.CompletedTask;
        }

        public Task<bool> HasAsOfDateForEveryPairAsync(IReadOnlyList<string> currencies, DateOnly asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FixedCatalog(string[] codes) : ICurrencyCatalog
    {
        public Currency? TryGet(string code) =>
            codes.Contains(code, StringComparer.OrdinalIgnoreCase) ? Currency.Create(code, code, 2) : null;

        public IReadOnlyList<Currency> List(bool enabledOnly = false) =>
            codes.Select(code => Currency.Create(code, code, 2)).ToList();

        public bool IsActive(string code) => codes.Contains(code, StringComparer.OrdinalIgnoreCase);

        public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
