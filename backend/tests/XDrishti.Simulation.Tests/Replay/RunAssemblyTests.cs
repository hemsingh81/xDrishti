using System.Security.Cryptography;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>Criterion 13 of P3-04 through the real replay: two replays of one input and two assemblies are equal, the key reacts to one tick of one close, and the golden record of the criterion 1 fixture (with the table of versions).</summary>
public sealed class RunAssemblyTests
{
    private static readonly Guid IdA = new("00000000-0000-0000-0000-0000000000a1");

    private static readonly Guid IdB = new("00000000-0000-0000-0000-0000000000b2");

    /// <summary>
    /// The SHA-256 of each golden record file, by engine version (computed with an independent tool, Python 3 <c>hashlib</c>, over the file as checked in). A replay rule changed without a new <see cref="SimulationEngine.Version"/> fails the
    /// golden comparison or this table; a new version without a new row fails here. Regenerate with <c>XD_UPDATE_GOLDEN=1</c>, then put the new hash in the row of the version.
    /// </summary>
    private static readonly Dictionary<string, string> GoldenHashes = new()
    {
        ["sim-2"] = "a042eae5c959d0d4cb380d85aeb28ddab4607e39ba5dc71f37f1a2204f51b2f3",
        ["sim-3"] = "c9aeca544c585699e455e754eef42cb3b63e8cb374910b540b0b56557be61df0",
        ["sim-4"] = "634181d931e1a209b6f79c3cda4785d9b00f36be24e36d9137741ef87685c7f3",
        ["sim-5"] = "d65ee6fcab505dfa568aa46653ebac26af91bf735e0e87eb12fc3f7d59f5c625",
        ["sim-6"] = "48f6a547425b67c5a3ee49f7d3f9f08041bd3a0a509ebe173b690da8767a990e",
    };

    private static UniverseRecord Universe(params (Guid Id, string Symbol)[] members) =>
        new(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [.. members.Select(m => new UniverseMember(m.Id, m.Symbol))]);

    private static SimulationRun Assemble(PreparedStrategy strategy, DateOnly from, DateOnly to, UniverseRecord universe, params IReadOnlyList<Bar>[] instrumentBars)
    {
        var replays = universe.Members.Select((m, i) => Rep.Replay(strategy, instrumentBars[i], from, to, id: m.Id, symbol: m.Symbol)).ToList();
        var header = new RunHeader(strategy.Record, SimulationEngine.Version, from, to, ReplayOptions.Default, false, strategy.Fills.ToCanonicalJson(), universe, Rep.DefaultCalendar);
        return RunAssembler.Assemble(header, replays);
    }

    private static SimulationRun AssembleOne(PreparedStrategy strategy, DateOnly from, DateOnly to, UniverseRecord universe, IReadOnlyList<Bar> bars, AdjustmentFactors factors)
    {
        var header = new RunHeader(strategy.Record, SimulationEngine.Version, from, to, ReplayOptions.Default, false, strategy.Fills.ToCanonicalJson(), universe, Rep.DefaultCalendar);
        return RunAssembler.Assemble(header, [Rep.Replay(strategy, bars, from, to, factors: factors, id: universe.Members[0].Id, symbol: universe.Members[0].Symbol)]);
    }

    private static Bar[] CriterionOneBars() => ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.F1(ReplayDays.D2), ReplayDays.Flat(ReplayDays.D3, 105.00));

    [Fact]
    public void Two_assemblies_of_equal_inputs_are_equal_in_every_field_and_have_the_same_key()
    {
        var strategy = Rep.Prepare(OrbV1.Create());
        var universe = Universe((IdA, "AAA"), (IdB, "BBB"));
        var bars = ReplayDays.Join(ReplayDays.F2(ReplayDays.D1), ReplayDays.F1(ReplayDays.D2), ReplayDays.F6(ReplayDays.D3));

        var first = Assemble(strategy, ReplayDays.D1, ReplayDays.D3, universe, bars, bars);
        var second = Assemble(strategy, ReplayDays.D1, ReplayDays.D3, universe, bars, bars);

        RunText.Run(first).ShouldBe(RunText.Run(second));
        first.RunKey.ShouldBe(second.RunKey);
        RunText.HasNegativeZero(RunText.Run(first)).ShouldBeFalse();
    }

    [Fact]
    public void The_totals_trades_and_open_at_end_of_a_real_run_are_the_sums_and_the_order_of_the_instruments()
    {
        var strategy = Rep.Prepare(OrbV1.Create());
        var universe = Universe((IdA, "AAA"), (IdB, "BBB"));
        var bars = ReplayDays.Join(ReplayDays.F2(ReplayDays.D1), ReplayDays.F1(ReplayDays.D2), ReplayDays.F6(ReplayDays.D3));

        var run = Assemble(strategy, ReplayDays.D1, ReplayDays.D3, universe, bars, bars);

        run.Trades.Select(t => (t.Trade.Symbol, t.Trade.TradeDate)).ShouldBe([("AAA", ReplayDays.D1), ("BBB", ReplayDays.D1), ("AAA", ReplayDays.D2), ("BBB", ReplayDays.D2)]);
        run.OpenAtEnd.Select(t => t.Trade.Symbol).ShouldBe(["AAA", "BBB"]);
        run.Totals.Fates.Trades.ShouldBe(4);
        run.Totals.OpenAtEnd.ShouldBe(2);
        run.Totals.Sessions.Ran.ShouldBe(6);
        run.Totals.BarsRead.ShouldBe(2 * ((3 * 375) - 20));
        run.Totals.Scans.Select(s => s.Candidates).ShouldBe([6L, 0L]);
        run.Totals.Walk.Candidates.ShouldBe(6);
        run.Totals.CandidatesInWindow.ShouldBe(6);
        run.Totals.TradesInWindow.ShouldBe(6);
        run.Totals.Instruments.ShouldBe(2);
        run.Totals.Skipped.ShouldBeEmpty();
        run.SchemaVersion.ShouldBe(6);
        run.EngineVersion.ShouldBe(SimulationEngine.Version);
        run.Fills.ShouldBe(strategy.Fills.ToCanonicalJson());
    }

    [Fact]
    public void An_end_of_data_trade_is_in_open_at_the_end_of_the_run_and_the_totals_count_it_once_and_not_as_a_trade()
    {
        var strategy = Rep.Prepare(OrbV1.Create());

        var run = Assemble(strategy, ReplayDays.D1, ReplayDays.D1, Universe((IdA, "AAA")), ReplayDays.F6(ReplayDays.D1));

        run.Trades.ShouldBeEmpty();
        run.OpenAtEnd.ShouldHaveSingleItem().Trade.ExitReason.ShouldBe(ExitReason.EndOfData);
        run.Totals.OpenAtEnd.ShouldBe(1);
        run.Totals.Fates.Trades.ShouldBe(0);
        run.Totals.TradesInWindow.ShouldBe(1);
    }

    [Fact]
    public void The_key_changes_when_one_close_of_one_bar_changes_by_one_tick()
    {
        var strategy = Rep.Prepare(OrbV1.Create());
        var universe = Universe((IdA, "AAA"));
        var bars = CriterionOneBars();
        var changed = (Bar[])bars.Clone();
        changed[700] = changed[700] with { Close = changed[700].Close + 0.05 };

        var before = Assemble(strategy, ReplayDays.D1, ReplayDays.D3, universe, bars);
        var after = Assemble(strategy, ReplayDays.D1, ReplayDays.D3, universe, changed);

        after.RunKey.ShouldNotBe(before.RunKey);
        after.Instruments[0].BarsSha256.ShouldNotBe(before.Instruments[0].BarsSha256);
    }

    [Fact]
    public void The_key_changes_with_an_applied_action_a_window_an_option_the_label_and_the_fill_rules()
    {
        var strategy = Rep.Prepare(OrbV1.Create());
        var universe = Universe((IdA, "AAA"));
        var bars = CriterionOneBars();
        var baseline = Assemble(strategy, ReplayDays.D1, ReplayDays.D3, universe, bars).RunKey;

        var withAction = AssembleOne(strategy, ReplayDays.D1, ReplayDays.D3, universe, bars, Rep.Split(ReplayDays.D4, 1, 2)).RunKey;
        var otherWindow = Assemble(strategy, ReplayDays.D2, ReplayDays.D3, universe, bars).RunKey;
        var otherFills = Assemble(Rep.Prepare(OrbV1.Create(), fills: FillRules.Default), ReplayDays.D1, ReplayDays.D3, universe, bars).RunKey;

        new[] { withAction, otherWindow, otherFills }.ShouldAllBe(k => k != baseline);
        new[] { withAction, otherWindow, otherFills }.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void The_key_is_the_same_for_the_default_fill_rules_given_or_omitted()
    {
        var universe = Universe((IdA, "AAA"));
        var bars = CriterionOneBars();
        var omitted = StrategyPreparation.Prepare(OrbV1.Create(), new Dictionary<string, double>(), Timeframe.M15).Strategy.ShouldNotBeNull();
        var given = StrategyPreparation.Prepare(OrbV1.Create(), new Dictionary<string, double>(), Timeframe.M15, FillRules.Default).Strategy.ShouldNotBeNull();

        Assemble(omitted, ReplayDays.D1, ReplayDays.D3, universe, bars).RunKey.ShouldBe(Assemble(given, ReplayDays.D1, ReplayDays.D3, universe, bars).RunKey);
    }

    [Fact]
    public void The_key_changes_with_any_slippage_number_and_the_limit_percent()
    {
        var universe = Universe((IdA, "AAA"));
        var bars = CriterionOneBars();
        FillRules Rules(double limit, double ticks, double frac, double extra) => FillRules.Create(limit, SlippageConfig.Create(ticks, frac, extra).Value).Value;
        var keys = new[]
        {
            Rules(0.1, 1, 0.05, 2), Rules(0.2, 1, 0.05, 2), Rules(0.1, 2, 0.05, 2), Rules(0.1, 1, 0.06, 2), Rules(0.1, 1, 0.05, 3),
        }.Select(rules => Assemble(Rep.Prepare(OrbV1.Create(), fills: rules), ReplayDays.D1, ReplayDays.D3, universe, bars).RunKey).ToList();

        keys.Distinct().Count().ShouldBe(5);
    }

    [Fact]
    public void The_key_changes_with_the_tick_of_an_instrument()
    {
        var strategy = Rep.Prepare(OrbV1.Create());
        var universe = Universe((IdA, "AAA"));
        var bars = CriterionOneBars();
        RunHeader Header() => new(strategy.Record, SimulationEngine.Version, ReplayDays.D1, ReplayDays.D3, ReplayOptions.Default, false, strategy.Fills.ToCanonicalJson(), universe, Rep.DefaultCalendar);

        string Key(TickSpec tick) => RunAssembler.Assemble(Header(), [Rep.Replay(strategy, bars, ReplayDays.D1, ReplayDays.D3, tick: tick, id: IdA, symbol: "AAA")]).RunKey;

        var five = Key(TickSpec.FromRupees(0.05m).Value);
        var scaled = Key(TickSpec.FromRupees(0.0500m).Value);
        var ten = Key(TickSpec.FromRupees(0.10m).Value);
        var fallback = Key(TickSpec.FromMasterPaise(null).Value);

        scaled.ShouldBe(five, "0.0500 is the tick 0.05");
        ten.ShouldNotBe(five);
        fallback.ShouldNotBe(five, "the fallback is recorded: the trades carry TickFallback");
    }

    [Fact]
    public void The_golden_record_of_the_criterion_one_fixture_is_checked_in_and_its_first_line_is_the_engine_version()
    {
        var strategy = Rep.Prepare(Rep.OrbWithEma10(), parameters: new Dictionary<string, double> { ["or_min"] = 15, ["rr"] = 2 });
        var run = Assemble(strategy, ReplayDays.D1, ReplayDays.D3, Universe((IdA, "AAA")), CriterionOneBars());

        var text = RunText.Run(run);

        RunText.HasNegativeZero(text).ShouldBeFalse();
        GoldenFile.Check("run-record", text);
        GoldenFile.Header.ShouldBe("engine " + SimulationEngine.Version);
    }

    [Fact]
    public void Each_engine_version_has_one_row_with_the_hash_of_its_golden_record_file()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Golden", "run-record.golden.txt");
        var file = File.ReadAllBytes(path);

        GoldenHashes.ShouldContainKey(SimulationEngine.Version, "a new engine version needs a new row: the hash of the regenerated golden record");
        Convert.ToHexStringLower(SHA256.HashData(file)).ShouldBe(GoldenHashes[SimulationEngine.Version], "a replay rule changed without a new SimulationEngine.Version");
    }
}
