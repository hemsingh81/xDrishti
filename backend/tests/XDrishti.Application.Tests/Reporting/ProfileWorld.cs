#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>
/// A basket world for the BK-07 handler tests: instruments with stored daily closes, baskets with stays (the members read applies the same rule as the store), the benchmark, the regimes and the strategy runs, all behind the ports.
/// The 25 sessions are the weekdays from Monday 2026-09-14 to Friday 2026-10-16.
/// </summary>
internal sealed class ProfileWorld
{
    public static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly[] Sessions = [.. Enumerable.Range(0, 35).Select(i => new DateOnly(2026, 9, 14).AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Take(25)];

    public IBasketQueries Baskets { get; } = Substitute.For<IBasketQueries>();

    public IBarQueries Bars { get; } = Substitute.For<IBarQueries>();

    public IPortfolioBenchmark Benchmark { get; } = Substitute.For<IPortfolioBenchmark>();

    public IBasketBenchmark BasketBenchmark { get; } = Substitute.For<IBasketBenchmark>();

    public IPortfolioBasketSource Portfolio { get; } = Substitute.For<IPortfolioBasketSource>();

    public IDerivedVersionStore Derived { get; } = Substitute.For<IDerivedVersionStore>();

    public IContextFigures Context { get; } = Substitute.For<IContextFigures>();

    public ReportWorld Runs { get; } = new();

    public BasketProfileSettings Settings { get; } = new();

    public ReportBenchmarkOptions Limits { get; } = new();

    public XDrishti.Domain.Confidence.CostStressOptions Cost { get; set; } = new();

    public ITradingCalendar Calendar { get; } = Substitute.For<ITradingCalendar>();

    public FakeTimeProvider Time { get; } = new(Now);

    public ProfileCache Cache { get; }

    /// <summary>Daily-bar queries made, by instrument.</summary>
    public List<Guid> DailyReads { get; } = [];

    public Dictionary<Guid, string> Symbols { get; } = [];

    private readonly Dictionary<Guid, List<(DateOnly Date, double Close)>> _closes = [];
    private readonly Dictionary<Guid, List<Membership>> _stays = [];
    private readonly Dictionary<Guid, (string Name, DateTimeOffset? Archived)> _baskets = [];

    private sealed record Membership(Guid Instrument, DateTimeOffset From, DateTimeOffset? To);

    public ProfileWorld()
    {
        Cache = new ProfileCache(Settings);
        Calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<XDrishti.Domain.Market.Calendar.TradingCalendar>(new XDrishti.Domain.Market.Calendar.TradingCalendar()));
        Benchmark.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<ConfiguredBenchmark?>((ConfiguredBenchmark?)null));
        BasketBenchmark.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<BasketBenchmark>(new BasketBenchmark(null, true, "Nifty 500", "Nifty 500 is not in the instrument master")));
        Context.AsOfManyAsync(Arg.Any<IReadOnlyCollection<DateOnly>>(), Arg.Any<CancellationToken>()).Returns(call =>
            new ValueTask<IReadOnlyDictionary<DateOnly, ContextRead>>(call.ArgAt<IReadOnlyCollection<DateOnly>>(0).ToDictionary(d => d, _ => ContextRead.Not(ContextRead.NoContext))));
        Portfolio.ExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<bool>(false));
        Derived.ListVersionsAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<BasketVersionPage?>((BasketVersionPage?)null));
        Bars.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var id = call.ArgAt<Guid>(0);
            DailyReads.Add(id);
            var (from, to) = (call.ArgAt<DateOnly>(1), call.ArgAt<DateOnly>(2));
            return new ValueTask<IReadOnlyList<BarRow>>([.. Closes(id).Where(c => c.Date >= from && c.Date < to).Select(c => Row(c.Date, c.Close))]);
        });
        Bars.ReadBarsAsync(Arg.Any<Guid>(), Arg.Any<Timeframe>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
            new ValueTask<IReadOnlyList<BarRow>>([.. Closes(call.ArgAt<Guid>(0)).OrderByDescending(c => c.Date).Take(call.ArgAt<int>(2)).Select(c => Row(c.Date, c.Close))]));
        Baskets.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => new ValueTask<BasketView?>(View(call.ArgAt<Guid>(0))));
        Baskets.MembersAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>()).Returns(call => new ValueTask<BasketMembers?>(Members(call.ArgAt<Guid>(0), call.ArgAt<DateTimeOffset?>(1), call.ArgAt<DateOnly?>(2))));
        Baskets.HistoryAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call => new ValueTask<BasketHistory?>(History(call.ArgAt<Guid>(0))));
    }

    private List<(DateOnly Date, double Close)> Closes(Guid id) => _closes.TryGetValue(id, out var list) ? list : [];

    public static BarRow Row(DateOnly day, double close) =>
        new(new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.FromMinutes(330)), close, close, close, close, 1000, 375, true, null);

    /// <summary>An instrument with closes <c>f(i)</c> on the first <paramref name="count"/> sessions.</summary>
    public Guid Instrument(string symbol, Func<int, double>? close = null, int count = 25)
    {
        var id = Guid.NewGuid();
        Symbols[id] = symbol;
        if (close is not null)
        {
            _closes[id] = [.. Enumerable.Range(0, count).Select(i => (Sessions[i], close(i)))];
        }

        return id;
    }

    /// <summary>An instrument with the given closes (any dates).</summary>
    public Guid Instrument(string symbol, IEnumerable<(DateOnly Date, double Close)> closes)
    {
        var id = Guid.NewGuid();
        Symbols[id] = symbol;
        _closes[id] = [.. closes];
        return id;
    }

    /// <summary>Stores one more daily bar of an instrument (a new stored bar).</summary>
    public void AddBar(Guid id, DateOnly day, double close) => _closes[id].Add((day, close));

    /// <summary>A basket whose members were added before the window and stay.</summary>
    public Guid Basket(string name, DateTimeOffset? archived = null, params Guid[] members)
    {
        var id = Guid.NewGuid();
        _baskets[id] = (name, archived);
        _stays[id] = [.. members.Select(m => new Membership(m, new DateTimeOffset(2026, 6, 1, 6, 0, 0, TimeSpan.Zero), null))];
        return id;
    }

    /// <summary>One more stay in a basket (an addition at <paramref name="from"/> and perhaps a removal).</summary>
    public void Stay(Guid basket, Guid instrument, DateTimeOffset from, DateTimeOffset? to = null) => _stays[basket].Add(new Membership(instrument, from, to));

    /// <summary>Makes the id a portfolio basket: current holdings (instrument, quantity), read from the returned list, which a test may change.</summary>
    public List<(Guid Instrument, long Quantity)> PortfolioBasket(Guid id, string name)
    {
        var holdings = new List<(Guid Instrument, long Quantity)>();
        Portfolio.ExistsAsync(id, Arg.Any<CancellationToken>()).Returns(new ValueTask<bool>(true));
        Portfolio.MembersAsync(id, Arg.Any<CancellationToken>()).Returns(_ => new ValueTask<BasketMembers?>(new BasketMembers(
            id, name, null, null, holdings.Count, [.. holdings.Select((h, n) => new BasketMemberView(Guid.NewGuid(), Summary(h.Instrument), n, Now, null, null, "portfolio", h.Quantity, "Dhan"))])));
        return holdings;
    }

    public static DateTimeOffset At(DateOnly day) => new(day.Year, day.Month, day.Day, 12, 0, 0, TimeSpan.FromMinutes(330));

    /// <summary>The index is the basket benchmark (the default Nifty 500 or, with isDefault false, the owner's choice); the portfolio benchmark is a different index so a read of the wrong one shows.</summary>
    public void IndexIsBenchmark(Guid index, string symbol = "NIFTY", string name = "Nifty 500", bool isDefault = true)
    {
        var instrument = new BenchmarkInstrument(index, symbol, name, "index", false, true, true);
        BasketBenchmark.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<BasketBenchmark>(new BasketBenchmark(instrument, isDefault, name, null)));
        Benchmark.FindAsync(index, Arg.Any<CancellationToken>()).Returns(new ValueTask<BenchmarkInstrument?>(instrument));
        Benchmark.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<ConfiguredBenchmark?>(new ConfiguredBenchmark(new BenchmarkInstrument(Guid.NewGuid(), "NSE:NIFTY", "Nifty 50", "index", false, true, true), true)));
    }

    private BasketView? View(Guid id) => _baskets.TryGetValue(id, out var b)
        ? new BasketView(id, b.Name, "#fff", "trading", null, 0, _stays[id].Count(s => s.To is null), Now, b.Archived, "manual", false, null, null)
        : null;

    private BasketMembers? Members(Guid id, DateTimeOffset? cutoff, DateOnly? asOf)
    {
        if (!_baskets.TryGetValue(id, out var b))
        {
            return null;
        }

        var items = _stays[id].Where(s => cutoff is { } at ? s.From < at && (s.To is null || s.To >= at) : s.To is null)
            .Select((s, n) => new BasketMemberView(Guid.NewGuid(), Summary(s.Instrument), n, s.From, s.To, null, "owner", null, null)).ToList();
        return new BasketMembers(id, b.Name, b.Archived, asOf, items.Count, items);
    }

    private BasketHistory? History(Guid id)
    {
        if (!_baskets.TryGetValue(id, out var b))
        {
            return null;
        }

        var entries = new List<BasketHistoryEntry>();
        foreach (var s in _stays[id])
        {
            entries.Add(new BasketHistoryEntry(Guid.NewGuid(), "added", s.From, s.Instrument, Symbols[s.Instrument], Symbols[s.Instrument], "share", null, "owner"));
            if (s.To is { } removed)
            {
                entries.Add(new BasketHistoryEntry(Guid.NewGuid(), "removed", removed, s.Instrument, Symbols[s.Instrument], Symbols[s.Instrument], "share", null, "owner"));
            }
        }

        return new BasketHistory(id, b.Name, [.. entries.OrderByDescending(e => e.At)]);
    }

    private InstrumentSummary Summary(Guid id) =>
        new(id, Symbols[id], Symbols[id], Symbols[id], "share", null, null, "Banks", 1, null, null, false, false, false, TrackedInfo.Never);

    public ProfileWindowResolver Resolver() => new(Bars, Benchmark, BasketBenchmark, Calendar, Settings, Limits, Time);

    public BasketMembershipLoader Memberships() => new(Baskets, Portfolio, Derived, Runs, Settings, Time);

    public GetBasketProfileHandler Profile() => new(Resolver(), Memberships(), Bars, Context, Runs.Loader, Runs, Cache, Settings, Limits, Cost, Time, NullLogger<GetBasketProfileHandler>.Instance);

    public GetBasketRankingHandler Ranking() => new(Resolver(), Memberships(), Bars, Settings, Limits, Cost, Time, NullLogger<GetBasketRankingHandler>.Instance);
}
