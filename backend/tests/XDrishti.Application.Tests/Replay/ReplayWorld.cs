#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Replay;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SizeScaling;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Replay;

/// <summary>
/// A small invented market for the replay tests (SR-01): symbols with a close for each weekday session, one strategy per cell with versions, cell statistics snapshots, a changelog, basket members, context days and a hold-out block. <see cref="Facts"/> builds the real
/// <see cref="ReplayFacts"/> over stubs of the stores that answer like the database does (by date range), so a test that mutates a later close or adds a later row sees whether the replay read it.
/// </summary>
internal sealed class ReplayWorld
{
    public static readonly DateOnly Start = new(2026, 10, 5); // a Monday

    public static readonly Guid Basket = new("aaaaaaaa-0000-0000-0000-000000000001");

    public static readonly IReadOnlyDictionary<string, Guid> Ids = new Dictionary<string, Guid>(StringComparer.Ordinal)
    {
        ["AAA"] = new("00000000-0000-0000-0000-0000000000a1"),
        ["BBB"] = new("00000000-0000-0000-0000-0000000000b1"),
        ["CCC"] = new("00000000-0000-0000-0000-0000000000c1"),
        ["DDD"] = new("00000000-0000-0000-0000-0000000000d1"),
        ["EEE"] = new("00000000-0000-0000-0000-0000000000e1"),
    };

    public static readonly string Hash1 = new('1', 64);

    public static readonly string Hash2 = new('2', 64);

    public static readonly SelectionRules Loose = new(OptionalFilters: [FilterCodes.Cost, FilterCodes.Event, FilterCodes.FnoBan, FilterCodes.Surveillance]);

    public static readonly CellId Cell = new("S1", "1D", "BUY", "swing", "FIXED_RR{rr=2}", Expression: Expression.Cash);

    public static readonly DateOnly[] Sessions = [.. Enumerable.Range(0, 200).Select(i => Start.AddDays(-130 + i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))];

    /// <summary>The close of each symbol on each session (the open of a session is its close: one price per day keeps the hand-worked numbers short).</summary>
    public Dictionary<string, SortedDictionary<DateOnly, double>> Close { get; } = [];

    public List<(Guid Id, string Symbol, DateTimeOffset From, DateTimeOffset? To)> Members { get; } = [];

    public DateOnly? KnownSince { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public bool NoHoldOut { get; set; }

    public SelectionRules Rules { get; set; } = Loose;

    public IBasketQueries? Baskets { get; private set; }

    public IStrategyStore? Strategies { get; private set; }

    public XDrishti.Application.Abstractions.Market.History.IBackfillSeams Seams { get; } = Substitute.For<XDrishti.Application.Abstractions.Market.History.IBackfillSeams>();

    public IRunStore Runs { get; } = Substitute.For<IRunStore>();

    public Dictionary<string, List<StoredVersion>> Versions { get; } = [];

    public List<(DateOnly AsOf, StoredCellStats Row)> Snapshots { get; } = [];

    public Dictionary<string, List<ChangelogEntry>> Changelog { get; } = [];

    public SortedDictionary<DateOnly, double> ContextVix { get; } = [];

    public DateOnly HoldOutStart { get; set; } = new(2027, 3, 1);

    public int EmbargoDays { get; set; } = 30;

    public FillConstraints Fill { get; set; } = new(MaxPairCorrelation: 1.0, MaxPerSector: 10, MaxIntraday: 10, MaxSwing: 10, MaxNetLongRiskPct: 50, MaxNetShortRiskPct: 50, MaxPerTradeRiskPct: 2.0);

    public XDrishti.Domain.Selection.SmartRules.SmartRulesOptions? Smart { get; set; }

    public BasketSettings BasketSettings { get; set; } = BasketSettings.Defaults;

    public static IReadOnlyList<DateOnly> RunSessions => [.. Sessions.Where(d => d >= Start)];

    public ReplayWorld WithDefaults(params string[] symbols)
    {
        foreach (var symbol in symbols)
        {
            Members.Add((Ids[symbol], symbol, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), null));
            Close[symbol] = new SortedDictionary<DateOnly, double>(Sessions.ToDictionary(d => d, d => 100.0 + Wiggle(symbol, d)));
        }

        Versions["S1"] = [Version("S1", 1, Hash1, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))];
        Snapshots.Add((Start.AddDays(-3), Stats(Start.AddDays(-3))));
        Changelog[Cell.Key] = [Transition(1, "research", "candidate", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero))];
        return this;
    }

    /// <summary>A small deterministic move per symbol and day, so the series are not all equal.</summary>
    public static double Wiggle(string symbol, DateOnly date) => Math.Sin((symbol[0] * 7) + (date.DayNumber * (symbol[0] % 5 + 1))) * 0.8 + (date.DayNumber % 3) * 0.1;

    public static StoredVersion Version(string strategy, int version, string hash, DateTimeOffset createdAt) => new(strategy, version, 1, hash, "text", "{}", null, null, createdAt);

    public static StoredCellStats Stats(DateOnly asOf, int n = 40, string snapshot = "snap") => new(
        snapshot + asOf.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.UnixEpoch, new CellStatsOptions(), "run:swing:FIXED_RR{rr=2}",
        new CellStatsRow(Cell, CellWindow.All, asOf, true, n, n / 2, n, 0.5, 0.5, 0.55, 0.50, 0.6, 2.0, -1.0, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true));

    public static ChangelogEntry Transition(long id, string from, string to, DateTimeOffset at) =>
        new(id, new NewChangelogEntry("transition", "cell", Cell.Key, from, to, "test", "owner:test", [], null, null, null, null, at));

    /// <summary>A series that rises every session by an amount that varies with <paramref name="k"/>: every session is a candidate, and a different <paramref name="k"/> gives a series that is not perfectly correlated with it.</summary>
    public void Rising(string symbol, double k, double start = 100)
    {
        var price = start;
        var map = new Dictionary<DateOnly, double>();
        for (var i = 0; i < Sessions.Length; i++)
        {
            price += 0.02 + (0.03 * (1 + Math.Sin(i * k)));
            map[Sessions[i]] = price;
        }

        Close[symbol] = new SortedDictionary<DateOnly, double>(map);
    }

    /// <summary>Gives every symbol the same series of closes from the given days on (used to make a cluster), or its own.</summary>
    public void Series(string symbol, Func<DateOnly, double> close) => Close[symbol] = new SortedDictionary<DateOnly, double>(Sessions.ToDictionary(d => d, close));

    /// <summary>The real facts over stubs that answer by date range like the stores do.</summary>
    public ReplayFacts Facts(double incompleteShare = 0.2)
    {
        var holdOut = TestHoldOutStores.NoReleases();
        holdOut.CurrentAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<HoldOutBlock?>(NoHoldOut ? null : new HoldOutBlock(HoldOutStart, Start.AddYears(-3), Start, 6, "h", DateTimeOffset.UnixEpoch)));

        var baskets = Substitute.For<IBasketQueries>();
        Baskets = baskets;
        baskets.ListAsync(false, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<BasketView>>([Blank<BasketView>() with { Id = Basket, Name = "B" }]));
        baskets.MembersAsync(Basket, Arg.Any<DateTimeOffset?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var cutoff = call.ArgAt<DateTimeOffset?>(1);
            var rows = Members.Where(m => cutoff is { } c ? m.From < c && (m.To is null || m.To >= c) : m.To is null).Select(m => Member(m)).ToList();
            return ValueTask.FromResult<BasketMembers?>(new BasketMembers(Basket, "B", ArchivedAt, null, rows.Count, rows, KnownSince));
        });
        baskets.GetAsync(Basket, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketView?>(Blank<BasketView>() with { Id = Basket, Name = "B" }));

        var strategies = Substitute.For<IStrategyStore>();
        Strategies = strategies;
        strategies.GetVersionAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(Versions.GetValueOrDefault(call.Arg<string>())?.MaxBy(v => v.Version)));
        strategies.ListVersionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult<IReadOnlyList<StoredVersion>>([.. Versions.GetValueOrDefault(call.Arg<string>()) ?? []]));
        strategies.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(Versions.Values.SelectMany(v => v).FirstOrDefault(v => v.Hash == call.Arg<string>())));

        var stats = Substitute.For<ICellStatsStore>();
        stats.ListLatestBeforeAsync(Arg.Any<DateOnly>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var before = call.Arg<DateOnly>();
            var newest = Snapshots.Where(s => s.AsOf < before).Select(s => (DateOnly?)s.AsOf).Max();
            return ValueTask.FromResult<IReadOnlyList<StoredCellStats>>(newest is null ? [] : [.. Snapshots.Where(s => s.AsOf == newest).Select(s => s.Row)]);
        });

        stats.ListLatestCashAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var newest = Snapshots.Select(x => (DateOnly?)x.AsOf).Max();
            return ValueTask.FromResult<IReadOnlyList<StoredCellStats>>(newest is null ? [] : [.. Snapshots.Where(x => x.AsOf == newest).Select(x => x.Row)]);
        });

        var changelog = Substitute.For<ILearningChangelog>();
        changelog.HistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult<IReadOnlyList<ChangelogEntry>>([.. Changelog.GetValueOrDefault(call.Arg<string>()) ?? []]));

        var minute = Substitute.For<IBarStore>();
        minute.ReadAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var symbol = Ids.First(p => p.Value == call.Arg<Guid>()).Key;
            var from = call.ArgAt<DateTimeOffset>(1).ToUnixTimeSeconds();
            var to = call.ArgAt<DateTimeOffset>(2).ToUnixTimeSeconds();
            IReadOnlyList<Bar> bars = [.. Close[symbol].Select(p => (Epoch: Day.Epoch(p.Key, 929), Price: p.Value)).Where(b => b.Epoch >= from && b.Epoch <= to).Select(b => new Bar(b.Epoch, b.Price, b.Price, b.Price, b.Price, 1000))];
            return ValueTask.FromResult(bars);
        });

        var days = Substitute.For<IBarQueries>();
        days.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var symbol = Ids.First(p => p.Value == call.Arg<Guid>()).Key;
            var from = call.ArgAt<DateOnly>(1);
            var toExclusive = call.ArgAt<DateOnly>(2);
            IReadOnlyList<BarRow> rows = [.. Close[symbol].Where(p => p.Key >= from && p.Key < toExclusive).Select(p => new BarRow(new DateTimeOffset(p.Key.ToDateTime(new TimeOnly(15, 29)), TimeSpan.FromHours(5.5)), p.Value, p.Value, p.Value, p.Value, 1_000_000, 375, true, null))];
            return ValueTask.FromResult(rows);
        });

        var calendar = Substitute.For<ITradingCalendar>();
        calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());

        var context = Substitute.For<IContextFigures>();
        context.AsOfAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var date = call.Arg<DateOnly>();
            var day = ContextVix.Keys.Where(k => k < date).Select(k => (DateOnly?)k).Max();
            return ValueTask.FromResult(day is { } d ? new ContextRead(Blank<ContextFigures>() with { AsOf = d, Basis = "t", Vix = ContextVix[d], Breadth = 60, BreadthUniverse = 100 }, null) : new ContextRead(null, ContextRead.NoContext));
        });

        var classifications = Substitute.For<IHoldingClassifications>();
        classifications.SectorsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult<IReadOnlyDictionary<Guid, string>>(call.Arg<IReadOnlyCollection<Guid>>().ToDictionary(id => id, id => "Sector" + (id == Ids["DDD"] ? "2" : "1"))));
        classifications.BasketsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<BasketRef>>>(call.Arg<IReadOnlyCollection<Guid>>().ToDictionary(id => id, id => (IReadOnlyList<BasketRef>)[new BasketRef(Basket, "B")])));

        var sheets = Substitute.For<ICostSheetStore>();
        sheets.FindAsync(PaperFixture.SheetId, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(PaperFixture.FlatSheet)));

        var basketSettings = Substitute.For<IBasketSettingsStore>();
        basketSettings.CurrentAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(new BasketSettingsState(BasketSettings, 1)));

        var sizing = Substitute.For<ISizingStore>();
        sizing.CurrentAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new SizingState(SizingSettings.Defaults, 0)));
        sizing.BudgetsAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyDictionary<Guid, double>>(new Dictionary<Guid, double>()));

        return new ReplayFacts(
            holdOut, new HoldOutOptions(TrainingEmbargoDays: EmbargoDays), baskets, Substitute.For<IInstrumentQueries>(), strategies, stats, changelog, Runs, minute, Seams, days, calendar, context, classifications, sheets, basketSettings, sizing,
            new SystemReplayOptions { IncompleteDataShare = incompleteShare }, Smart, Rules, Fill);
    }

    private static BasketMemberView Member((Guid Id, string Symbol, DateTimeOffset From, DateTimeOffset? To) m) =>
        new(Guid.NewGuid(), Blank<InstrumentSummary>() with { Id = m.Id, Symbol = m.Symbol }, 0, m.From, m.To, null, "owner", null, null);

    /// <summary>Only the fields the code under test reads are set; the other fields of these wide records are not looked at.</summary>
    public static T Blank<T>() => (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));
}
