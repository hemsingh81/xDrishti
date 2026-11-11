#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using System.Diagnostics;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Planning;
using XDrishti.Application.Replay;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SizeScaling;
using XDrishti.Infrastructure.Replay;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.File;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Replay;

/// <summary>
/// The explicit benchmark of SR-01 part B (the speed target of the non-functional section), not part of <c>scripts/check.sh</c>; run with <c>--explicit only</c>. It replays one year (about 250 sessions) over 100 invented instruments of one basket with the real facts, the real
/// candidate source (the pure ticket builder over a real seeded strategy), the real session step, selection and the rolling window of bars, on a random walk of 375 one-minute bars a session held in memory. It prints the time per session and the total, and <b>asserts nothing about time</b>.
/// What it does not measure: the database read of the bars (the stores here answer from memory) and the simulation of a held swing trade (the simulation is a stub that finds no trade, so no trade is held). Those add to the figure on the owner's machine.
/// </summary>
public sealed class ReplayBenchmarks
{
    private const int Instruments = 100;
    private static readonly DateOnly Start = new(2026, 1, 5);
    private static readonly DateOnly End = new(2026, 12, 31);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    public async Task One_year_over_one_hundred_instruments_prints_the_time_per_session()
    {
        var ids = Enumerable.Range(0, Instruments).Select(i => new Guid(i + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1])).ToList();
        // the minutes are generated on demand (a random walk seeded by instrument and day), so the working set measured below is the engine's and not a resident copy of the data
        var daily = ids.Select((_, i) => Sessions().Select(d => { var bars = Day(i, d); return new BarRow(new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5)), bars[0].Open, bars.Max(b => b.High), bars.Min(b => b.Low), bars[^1].Close, bars.Sum(b => b.Volume), bars.Length, true, null); }).ToArray()).ToList();
        var (basket, seedText) = (Guid.NewGuid(), XDrishti.Infrastructure.Research.StrategySeeder.EmbeddedTexts().First(t => t.Name.StartsWith("BASE_BREAKOUT", StringComparison.Ordinal)).Text);
        var hash = CanonicalText.Sha256Hex(CanonicalStrategy.Json(StrategyFile.Read(seedText).Definition!));
        var version = new StoredVersion("BASE_BREAKOUT", 1, 1, hash, seedText, "{}", null, null, DateTimeOffset.UnixEpoch);
        var cell = new CellId("BASE_BREAKOUT", "1D", "BUY", "swing", "FIXED_RR");
        var statsRow = new CellStatsRow(cell, CellWindow.All, Start.AddDays(-3), true, 40, 20, 40, 0.5, 0.5, 0.55, 0.5, 0.6, 2, -1, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);
        var stats = new StoredCellStats("bench", DateTimeOffset.UnixEpoch, new CellStatsOptions(), "", statsRow);
        IReadOnlyList<StoredCellStats> snapshot = [stats];

        var holdOut = Substitute.For<IHoldOutStore>();
        holdOut.CurrentAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<HoldOutBlock?>(new HoldOutBlock(new DateOnly(2027, 6, 1), new DateOnly(2024, 1, 1), End, 6, "h", DateTimeOffset.UnixEpoch)));
        var members = ids.Select((id, i) => new BasketMemberView(Guid.NewGuid(), Blank<InstrumentSummary>() with { Id = id, Symbol = "S" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) }, 0, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), null, null, "owner", null, null)).ToList();
        var baskets = Substitute.For<IBasketQueries>();
        baskets.MembersAsync(basket, Arg.Any<DateTimeOffset?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketMembers?>(new BasketMembers(basket, "B", null, null, members.Count, members, null)));
        baskets.GetAsync(basket, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketView?>(Blank<BasketView>() with { Id = basket, Name = "B" }));
        var strategies = Substitute.For<IStrategyStore>();
        strategies.GetVersionAsync("BASE_BREAKOUT", null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredVersion?>(version));
        strategies.GetByHashAsync(hash, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredVersion?>(version));
        var cellStats = Substitute.For<ICellStatsStore>();
        cellStats.ListLatestBeforeAsync(Arg.Any<DateOnly>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(snapshot));
        var changelog = Substitute.For<ILearningChangelog>();
        changelog.HistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ChangelogEntry>>([new ChangelogEntry(1, new NewChangelogEntry("transition", "cell", cell.Key, "research", "candidate", "bench", "owner:bench", [], null, null, null, null, new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)))]));
        var minutes = Substitute.For<IBarStore>();
        minutes.ReadAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var instrument = ids.IndexOf(call.Arg<Guid>());
            var (from, to) = (call.ArgAt<DateTimeOffset>(1).ToUnixTimeSeconds(), call.ArgAt<DateTimeOffset>(2).ToUnixTimeSeconds());
            IReadOnlyList<Bar> read = [.. Sessions().Where(d => Day0(d) + (24 * 3600) > from && Day0(d) <= to).SelectMany(d => Day(instrument, d)).Where(b => b.EpochSeconds >= from && b.EpochSeconds <= to)];
            return ValueTask.FromResult(read);
        });
        var days = Substitute.For<IBarQueries>();
        days.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var rows = daily[ids.IndexOf(call.Arg<Guid>())];
            var (from, toExclusive) = (call.ArgAt<DateOnly>(1), call.ArgAt<DateOnly>(2));
            return ValueTask.FromResult<IReadOnlyList<BarRow>>([.. rows.Where(r => DateOnly.FromDateTime(r.Ts.DateTime) >= from && DateOnly.FromDateTime(r.Ts.DateTime) < toExclusive)]);
        });
        var calendar = Substitute.For<ITradingCalendar>();
        calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        var context = Substitute.For<IContextFigures>();
        context.AsOfAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new ContextRead(null, ContextRead.NoContext));
        var classifications = Substitute.For<IHoldingClassifications>();
        classifications.SectorsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult<IReadOnlyDictionary<Guid, string>>(call.Arg<IReadOnlyCollection<Guid>>().ToDictionary(id => id, id => "Sector" + (id.GetHashCode() % 10))));
        classifications.BasketsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<BasketRef>>>(call.Arg<IReadOnlyCollection<Guid>>().ToDictionary(id => id, id => (IReadOnlyList<BasketRef>)[new BasketRef(basket, "B")])));
        var basketSettings = Substitute.For<IBasketSettingsStore>();
        basketSettings.CurrentAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new BasketSettingsState(BasketSettings.Defaults, 1)));
        var sizing = Substitute.For<ISizingStore>();
        sizing.CurrentAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new SizingState(SizingSettings.Defaults, 0)));
        sizing.BudgetsAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyDictionary<Guid, double>>(new Dictionary<Guid, double>()));

        var facts = new ReplayFacts(
            holdOut, new HoldOutOptions(TrainingEmbargoDays: 30), baskets, Substitute.For<IInstrumentQueries>(), strategies, cellStats, changelog, Substitute.For<IRunStore>(), minutes, Substitute.For<IBackfillSeams>(), days, calendar, context, classifications,
            Substitute.For<ICostSheetStore>(), basketSettings, sizing, new SystemReplayOptions(), rules: new SelectionRules(OptionalFilters: [FilterCodes.Cost, FilterCodes.Event, FilterCodes.FnoBan, FilterCodes.Surveillance]));
        var simulation = Substitute.For<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
        simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<RunSimulationQuery>().Request;
            var id = request.InstrumentIds![0];
            return ValueTask.FromResult(Result.Success(FabricatedRun.Assemble(FabricatedRun.List((id, "X")), [FabricatedRun.Ran(id, "X", request.From.AddDays(-30), request.To)], [], [], from: request.From, to: request.To, strategy: "BASE_BREAKOUT")));
        });
        var runner = new ReplaySessionRunner(facts, new ReplayCandidateSource(facts), new TicketReplayer(strategies, baskets, simulation));
        var driver = new ReplayDriver(facts, runner);
        var parameters = new ReplayParams(Start, End, new ReplayScope([basket], []), 1_000_000m, ReplayRiskProfile.Standard, null, ReplayVersionRule.Start, ReplayMode.Frozen);

        var prepared = await driver.PrepareAsync(parameters, null, null, Ct);
        prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Error.Message : string.Empty);
        var watch = Stopwatch.StartNew();
        var slowest = TimeSpan.Zero;
        var candidates = 0;
        long live = 0; // the live managed bytes after a full collection, sampled every 25 sessions: what the run really holds
        var sampled = 0;
        long peak = 0; // the peak working set after any session (PeakWorkingSet64 reads 0 on macOS)
        foreach (var session in prepared.Value.Sessions)
        {
            var one = Stopwatch.StartNew();
            var record = await driver.StepAsync(prepared.Value, session, Ct);
            record.IsSuccess.ShouldBeTrue(record.IsFailure ? record.Error.Message : string.Empty);
            candidates += record.Value.Candidates.Count;
            slowest = one.Elapsed > slowest ? one.Elapsed : slowest;
            peak = Math.Max(peak, Environment.WorkingSet);
            if (++sampled % 25 == 0)
            {
                live = Math.Max(live, GC.GetTotalMemory(true));
            }
        }

        watch.Stop();
        var line = FormattableString.Invariant($"SR-01 benchmark: {prepared.Value.Sessions.Count} sessions x {Instruments} instruments in {watch.Elapsed.TotalSeconds:F1} s, {watch.Elapsed.TotalSeconds / prepared.Value.Sessions.Count:F2} s per session (slowest {slowest.TotalSeconds:F2} s), {candidates} candidates, peak working set {peak / 1024 / 1024} MiB, live managed {live / 1024 / 1024} MiB");
        Console.WriteLine(line);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "replay-benchmark.txt"), line + Environment.NewLine, Ct); // the runner of the test hides the console of a passing test
    }

    private static IEnumerable<DateOnly> Sessions()
    {
        for (var d = new DateOnly(2025, 1, 1); d <= End; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                yield return d;
            }
        }
    }

    private static long Day0(DateOnly d) => new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5)).ToUnixTimeSeconds();

    /// <summary>The 375 bars of one session of one instrument: a random walk seeded by instrument and day, from a slow drift of the day's level.</summary>
    private static Bar[] Day(int instrument, DateOnly d)
    {
        var state = (uint)((instrument * 7919) + (d.DayNumber * 104729) + 17);
        var price = 100.0 + instrument + (5 * Math.Sin(d.DayNumber / 20.0));
        var open = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5)).AddMinutes(SessionClock.OpenMinute);
        var bars = new Bar[SessionClock.RegularBars];
        for (var m = 0; m < bars.Length; m++)
        {
            state = ((state * 1103515245u) + 12345u) & 0x7fffffffu;
            var next = Math.Max(1, price + (((state >> 8) % 9) - 4) * 0.01);
            bars[m] = new Bar(open.AddMinutes(m).ToUnixTimeSeconds(), price, Math.Max(price, next) + 0.01, Math.Min(price, next) - 0.01, next, 1000 + (state % 4000));
            price = next;
        }

        return bars;
    }

    private static T Blank<T>() => (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));
}
