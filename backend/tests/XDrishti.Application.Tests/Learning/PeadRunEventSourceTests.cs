#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>
/// P15-09: the real <c>IPeadEventSource</c> finds the newest stored run of each variant and bucket by its definition hash, reads no other strategy and no run of another engine, turns the closed trades into events with their sessions counted in the calendar and gross R, and answers an empty
/// set with the reason when nothing is stored, which protocol P states as NOT_EVALUABLE.
/// </summary>
public sealed class PeadRunEventSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly ISimulationEngine _engine = Substitute.For<ISimulationEngine>();

    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private static string Hash(double measure, double hold, double buckets) => CanonicalText.Sha256Hex($"pead|{measure}|{hold}|{buckets}");

    private PeadRunEventSource Source()
    {
        _engine.EngineVersion.Returns("sim-9");
        _engine.Prepare("PEAD", Arg.Any<IReadOnlyDictionary<string, double>>(), Timeframe.D1, null, null).Returns(ci =>
        {
            var p = ci.ArgAt<IReadOnlyDictionary<string, double>>(1);
            var prepared = Substitute.For<IPreparedStrategy>();
            prepared.Record.Returns(new StrategyRecord("PEAD", p, "{}", Hash(p["measure"], p["hold"], p["buckets"]), Timeframe.D1, [Side.Buy]));
            return Result.Success(prepared);
        });
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        return new PeadRunEventSource(_runs, _engine, _calendar);
    }

    private static StoredRunHeader Header(string key, string strategy, string hash) =>
        new(1, key, "sim-9", 8, 5, strategy, hash, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private static readonly (double M, double H, double B)[] Combos = [(1, 5, 1), (1, 5, 2), (1, 20, 1), (1, 20, 2), (2, 5, 1), (2, 5, 2), (2, 20, 1), (2, 20, 2)];

    private static string KeyOf(char set, int i) => new string((char)(set + i), 64);

    /// <summary>The eight runs of one set (newest first among themselves), sharing the window <paramref name="from"/> to 2026-01-01.</summary>
    private static StoredRunHeader[] Eight(char set, DateOnly from) =>
        [.. Combos.Select((c, i) => Header(KeyOf(set, i), "PEAD", Hash(c.M, c.H, c.B)) with { From = from })];

    private void StreamEmptyExcept(char set, params (int Index, SimTrade Trade)[] trades)
    {
        for (var i = 0; i < Combos.Length; i++)
        {
            _runs.StreamTradesAsync(KeyOf(set, i), false, Ct).Returns(Trades([.. trades.Where(t => t.Index == i).Select(t => t.Trade)]));
        }
    }

    private void Listed(params StoredRunHeader[] headers) =>
        _runs.ListAsync(Arg.Is<RunListFilter>(f => f.StrategyId == "PEAD" && f.EngineVersion == "sim-9"), Arg.Any<PageRequest>(), Ct)
            .Returns(ValueTask.FromResult(Result.Success(new Page<StoredRunHeader>(headers, null))));

    private static async IAsyncEnumerable<RunTrade> Trades(params SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private static SimTrade Pead(Guid id, string symbol, double r) =>
        FabricatedRun.Trade(id, symbol, Side.Buy, new DateOnly(2026, 3, 3), ExitReason.MaxHold, rFilled: r, strategy: "PEAD") with { ExitEpochSeconds = Day.Epoch(new DateOnly(2026, 3, 9), 899) };

    [Fact]
    public async Task With_no_stored_run_the_answer_is_empty_with_the_reason()
    {
        Listed();

        var set = await Source().ReadAsync(Ct);

        set.Events.ShouldBeEmpty();
        set.SourceNote.ShouldContain("no stored run of PEAD");
    }

    [Fact]
    public async Task A_set_of_eight_is_read_by_hash_with_the_newest_run_of_each_variant_and_bucket_and_other_strategies_are_not()
    {
        var older = Eight('A', new DateOnly(2025, 1, 1));
        var other = Header("z".PadRight(64, 'z'), "PEAD", "e".PadRight(64, 'e'));
        Listed([.. Eight('a', new DateOnly(2025, 1, 1)), other, .. older]);
        StreamEmptyExcept('a', (0, Pead(FabricatedRun.IdA, "NSE:A", 0.8)), (1, Pead(FabricatedRun.IdB, "NSE:B", -0.2)));

        var set = await Source().ReadAsync(Ct);

        set.Events.Count.ShouldBe(2);
        _ = _runs.DidNotReceive().StreamTradesAsync(KeyOf('A', 0), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        _ = _runs.DidNotReceive().StreamTradesAsync(other.RunKey, Arg.Any<bool>(), Arg.Any<CancellationToken>());
        var top = set.Events.Single(e => e.Bucket == PeadBucket.Top);
        var middle = set.Events.Single(e => e.Bucket == PeadBucket.Middle);
        (top.Variant, top.EntryDate, top.R).ShouldBe(("sue_yoy/5", new DateOnly(2026, 3, 3), 0.8));
        (middle.Variant, middle.R).ShouldBe(("sue_yoy/5", -0.2));
        top.ExitSession.ShouldBe(top.EntrySession + 4);
        top.Key.ShouldNotBe(middle.Key);
        set.SourceNote.ShouldContain("8 stored runs of PEAD for engine sim-9");
    }

    [Fact]
    public async Task A_newer_partial_run_is_ignored_and_the_newest_complete_set_is_read()
    {
        // a newer run of one variant over another window is not a set; the older eight (same window) are
        var partial = Header("p".PadRight(64, 'p'), "PEAD", Hash(1, 5, 1)) with { From = new DateOnly(2024, 1, 1) };
        Listed([partial, .. Eight('a', new DateOnly(2025, 1, 1))]);
        StreamEmptyExcept('a', (0, Pead(FabricatedRun.IdA, "NSE:A", 0.8)));

        var set = await Source().ReadAsync(Ct);

        set.Events.Count.ShouldBe(1);
        _ = _runs.DidNotReceive().StreamTradesAsync(partial.RunKey, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Eight_runs_that_do_not_share_one_window_are_not_a_coherent_set()
    {
        var mixed = Eight('a', new DateOnly(2025, 1, 1)).Select((h, i) => i == 3 ? h with { To = new DateOnly(2025, 12, 1) } : h);
        Listed([.. mixed]);

        var set = await Source().ReadAsync(Ct);

        set.Events.ShouldBeEmpty();
        set.SourceNote.ShouldContain("no coherent set of the eight PEAD runs");
    }

    [Fact]
    public async Task The_variants_are_the_four_of_protocol_p_in_its_order()
    {
        Listed(Eight('a', new DateOnly(2025, 1, 1)));
        StreamEmptyExcept('a', (6, Pead(FabricatedRun.IdA, "NSE:A", 0.5)));

        var set = await Source().ReadAsync(Ct);

        set.Events.Single().Variant.ShouldBe(ProtocolP.Variants[3], "results_car3/20");
        foreach (var variant in ProtocolP.Variants)
        {
            variant.ShouldMatch("^(sue_yoy|results_car3)/(5|20)$");
        }
    }

    [Fact]
    public async Task Too_few_events_for_twenty_seasons_leave_protocol_p_not_evaluable_with_the_seasons_named()
    {
        Listed(Eight('a', new DateOnly(2025, 1, 1)));
        StreamEmptyExcept('a', (0, Pead(FabricatedRun.IdA, "NSE:A", 0.5)));
        var set = await Source().ReadAsync(Ct);

        var result = ProtocolP.Evaluate(set.Events, 4, new ProtocolPOptions());

        result.IsSuccess.ShouldBeTrue();
        result.Value.Outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        result.Value.Outcome.Missing.ShouldContain(m => m.StartsWith("seasons 1 of 20", StringComparison.Ordinal));
    }
}
