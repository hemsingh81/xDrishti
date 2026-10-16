using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Live;
using XDrishti.Application.Tests.Market;
using XDrishti.Application.Tests.Portfolio.Live;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Live.FeedTestData;

namespace XDrishti.Application.Tests.Live;

/// <summary>The three providers of the active set (P2-10 criterion 7): holdings, alerts and indices.</summary>
public sealed class ActiveSetProvidersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Holdings_are_proposed_in_quote_mode_with_the_value_held()
    {
        var prices = new FakeLivePrices();
        prices.Securities.Add(new HeldSecurity(Reliance, "2885", 125_000m));

        var candidates = await new HoldingsProvider(prices).ListAsync(Ct);

        var candidate = candidates.ShouldHaveSingleItem();
        candidate.ShouldBe(new ActiveSetCandidate(Reliance, FeedSegments.NseEquity, "2885", FeedMode.Quote, ActiveSetReason.Holding, 125_000m));
    }

    [Fact]
    public async Task With_no_holdings_nothing_is_proposed()
    {
        (await new HoldingsProvider(new FakeLivePrices()).ListAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Alerts_that_can_be_quoted_are_proposed_in_ticker_mode_and_the_others_are_not()
    {
        var alerts = new FakeAlerts();
        alerts.Active.Add(new ActiveAlert(Guid.NewGuid(), Reliance, "RELIANCE", "2885", AlertDirection.AtOrAbove, 3000m));
        alerts.Active.Add(new ActiveAlert(Guid.NewGuid(), Infosys, "INFY", null, AlertDirection.AtOrBelow, 1000m));

        var candidates = await new AlertsProvider(alerts).ListAsync(Ct);

        candidates.ShouldHaveSingleItem().ShouldBe(new ActiveSetCandidate(Reliance, FeedSegments.NseEquity, "2885", FeedMode.Ticker, ActiveSetReason.Alert));
    }

    [Fact]
    public async Task Indices_are_the_context_indices_and_the_benchmark_in_ticker_mode()
    {
        var indices = new FakeIndices();
        indices.Add("NIFTY 50", Nifty, "13");
        indices.Add("NIFTY BANK", Guid.NewGuid(), "25");
        indices.Add("INDIA VIX", Vix, "21");
        var midcap = Guid.NewGuid();
        indices.AddById(midcap, "MIDCAP", "442");
        var provider = new IndicesProvider(indices, new FakeBenchmark(midcap), new MissingIndexReport(), NullLogger<IndicesProvider>.Instance);

        var candidates = await provider.ListAsync(Ct);

        candidates.Select(c => c.SecurityId).ShouldBe(["13", "25", "21", "442"], ignoreOrder: true);
        candidates.ShouldAllBe(c => c.Segment == FeedSegments.Index && c.Mode == FeedMode.Ticker && c.Reason == ActiveSetReason.Index);
    }

    [Fact]
    public async Task The_benchmark_that_is_already_a_context_index_is_proposed_once()
    {
        var indices = new FakeIndices();
        indices.Add("NIFTY 50", Nifty, "13");
        indices.AddById(Nifty, "NSE:NIFTY", "13");
        var provider = new IndicesProvider(indices, new FakeBenchmark(Nifty), new MissingIndexReport(), NullLogger<IndicesProvider>.Instance);

        (await provider.ListAsync(Ct)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_index_missing_from_the_master_is_skipped_with_one_log_line_for_the_life_of_the_process()
    {
        var indices = new FakeIndices();
        indices.Add("NIFTY 50", Nifty, "13");
        var log = new CapturingLogger<IndicesProvider>();
        var report = new MissingIndexReport();

        var first = await new IndicesProvider(indices, new FakeBenchmark(null), report, log).ListAsync(Ct);
        await new IndicesProvider(indices, new FakeBenchmark(null), report, log).ListAsync(Ct);

        first.ShouldHaveSingleItem();
        log.Events.Count(e => e.Event == 4109).ShouldBe(2); // NIFTY BANK and INDIA VIX, once each, not again at the second look
        log.Events.Select(e => e.Message).ShouldAllBe(m => !m.Contains("NIFTY 50", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_loader_unites_the_providers_and_warns_once_about_a_cut()
    {
        var provider = new FakeActiveSetProvider();
        provider.Candidates.AddRange([Holding(Reliance, "2885", 10m), Holding(Infosys, "1594", 5m), Index(Nifty, "13")]);
        var log = new CapturingLogger<ActiveSetLoader>();
        var services = new Platform.Services.MiniProvider().ScopedInstance<IActiveSetProvider>(provider);
        var loader = new ActiveSetLoader(services, LiveFeedSettings.Default with { MaxInstruments = 2 }, log);

        var set = await loader.LoadAsync(Ct);
        await loader.LoadAsync(Ct);

        set.Members.Select(m => m.InstrumentId).ShouldBe([Nifty, Reliance]);
        set.Cut.ShouldBe(1);
        log.Events.Count(e => e.Event == 4108).ShouldBe(1);
    }

    private sealed class FakeAlerts : IPriceAlerts
    {
        public List<ActiveAlert> Active { get; } = [];

        public ValueTask<IReadOnlyList<ActiveAlert>> ListActiveAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<ActiveAlert>>([.. Active]);

        public ValueTask<bool> TryTriggerAsync(Guid alertId, decimal price, DateTimeOffset at, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }

    private sealed class FakeIndices : IIndexSecurities
    {
        private readonly List<IndexSecurity> _byName = [];
        private readonly List<IndexSecurity> _byId = [];

        public void Add(string label, Guid id, string securityId) => _byName.Add(new IndexSecurity(label, id, securityId));

        public void AddById(Guid id, string label, string securityId) => _byId.Add(new IndexSecurity(label, id, securityId));

        public ValueTask<IReadOnlyList<IndexSecurity>> FindAsync(IReadOnlyList<IndexName> names, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IndexSecurity>>([.. _byName.Where(i => names.Any(n => n.Label == i.Label))]);

        public ValueTask<IndexSecurity?> FindByIdAsync(Guid instrumentId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_byId.FirstOrDefault(i => i.InstrumentId == instrumentId));
    }

    private sealed class FakeBenchmark(Guid? instrument) : IPortfolioBenchmark
    {
        public ValueTask<ConfiguredBenchmark?> GetAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(instrument is { } id ? new ConfiguredBenchmark(new BenchmarkInstrument(id, "NSE:X", "X", "index", false, true, true), false) : null);

        public ValueTask<BenchmarkInstrument?> FindAsync(Guid instrumentId, CancellationToken cancellationToken) => ValueTask.FromResult<BenchmarkInstrument?>(null);

        public ValueTask SetAsync(Guid instrumentId, string actor, DateTimeOffset now, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
