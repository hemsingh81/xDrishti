#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Confidence;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>CF-12 part 12a (ADR 0218) over fake ports: the series assembly shared with the sequential test, the strategy reference, the unavailable states, the kill-rule evaluations with their numbers, the behavioural series, and that nothing is written.</summary>
public sealed class GetDecayReportHandlerTests
{
    private const string Run = "run-a";
    private static readonly DateOnly Start = new(2026, 11, 2);
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 4, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IShadowResultReader _reader = Substitute.For<IShadowResultReader>();
    private readonly IPaperLedgerData _ledger = Substitute.For<IPaperLedgerData>();
    private readonly IBacktestPaperData _paper = Substitute.For<IBacktestPaperData>();
    private readonly ICostConfirmationFact _costs = Substitute.For<ICostConfirmationFact>();
    private readonly ICellStatsStore _stats = Substitute.For<ICellStatsStore>();
    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly IKillProposalReader _proposals = Substitute.For<IKillProposalReader>();

    public GetDecayReportHandlerTests()
    {
        _ledger.EpochAsync(null, Ct).Returns(new EpochRow(1, Start, DateTimeOffset.UnixEpoch, "first", "default", "system"));
        _paper.ReadAsync(Arg.Any<long>(), Ct).Returns(new PaperCompareData([], 0, null));
        _costs.ConfirmedOnAsync(Arg.Any<string>(), Ct).Returns(ValueTask.FromResult<DateOnly?>(new DateOnly(2026, 10, 30)));
        _reader.ResultsAsync(Ct).Returns([]);
        _stats.ListLatestBeforeAsync(Start, null, null, Ct).Returns(ValueTask.FromResult<IReadOnlyList<StoredCellStats>>([]));
    }

    private ValueTask<Result<DecayReportView>> RunAsync(string? strategy = "ORB", DecayOptions? options = null, KillOptions? kill = null) =>
        new GetDecayReportHandler(_reader, _ledger, _paper, _costs, new BacktestReferenceReader(_stats, _runs), options ?? new DecayOptions(), new SequentialOptions(), kill ?? new KillOptions(), _proposals).HandleAsync(new GetDecayReportQuery(strategy), Ct);

    private static KillProposalRow Fired(int seriesTrades, long? epoch = 1) =>
        new(9, new NewKillProposal("ORB", epoch, "cost_stress", false, -0.5, 0, 100, "t", ["ORB|15m|BUY|intraday|FIXED_RR|*"], new string('a', 64), seriesTrades), new DateOnly(2026, 11, 3), DateTimeOffset.UnixEpoch, "proposed", null, null, null);

    [Fact]
    public async Task The_cost_multiplier_of_the_settings_is_used_by_rule_a()
    {
        Reference(40);
        Series(130, -0.5);

        var kill = (await RunAsync(kill: new KillOptions(StressMultiplier: 2))).Value.Kill.Single(k => k.Rule == "cost_stress");

        kill.Value!.Value.ShouldBe(-0.5 - 0.12, 1e-9);
        kill.Text.ShouldContain("2.0 times cost");
    }

    [Fact]
    public async Task A_rule_that_fired_with_fewer_than_twenty_new_trades_since_is_held_and_with_twenty_it_fires_again()
    {
        Reference(40);
        Series(130, -0.5);
        _proposals.LatestAsync("ORB", "cost_stress", 1, Ct).Returns(Fired(seriesTrades: 115), Fired(seriesTrades: 110));

        var held = (await RunAsync()).Value.Kill.Single(k => k.Rule == "cost_stress");
        var again = (await RunAsync()).Value.Kill.Single(k => k.Rule == "cost_stress");

        held.State.ShouldBe("held");
        held.Text.ShouldContain("15 new trades");
        again.State.ShouldBe("fires");
    }

    private static TradeCosting Costing(double rNet) =>
        new(CostSegment.IntradayEquity, "flat-10", 100, 1000m, 10000m, 10000m, LegCharges.Zero, LegCharges.Zero, 100m, 0.1, rNet);

    private static StoredRunHeader Header(string key, string? sheet) =>
        new(1, key, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", sheet, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    /// <summary>A reference of <paramref name="count"/> backtest trades of the strategy ORB before the epoch, alternating +1.12 and -0.88 (mean 0.12, sd about 1).</summary>
    private void Reference(int count, bool costed = true, bool varied = true)
    {
        var trades = Enumerable.Range(0, count).Select(i =>
        {
            var r = varied ? (i % 2 == 0 ? 1.12 : -0.88) : 0.12;
            return FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Start.AddDays(-1 - i), rFilled: r, rExact: r, costing: costed ? Costing(r) : null);
        }).ToArray();
        _runs.FindAsync(Run, Ct).Returns(ValueTask.FromResult<StoredRunHeader?>(Header(Run, costed ? "flat-10" : null)));
        _runs.StreamTradesAsync(Run, false, Ct).Returns(_ => Stream(trades));
        var row = new CellStatsRow(new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR"), CellWindow.All, Start.AddDays(-2), costed, count, 0, count, null, 0.5, 0.5, 0, 1, null, null, 0.12, null, 0, null, null, null, null, true);
        _stats.ListLatestBeforeAsync(Start, null, null, Ct).Returns(ValueTask.FromResult<IReadOnlyList<StoredCellStats>>([new StoredCellStats("snapkey1234567890", DateTimeOffset.UnixEpoch, new CellStatsOptions(), $"{Run}:intraday:FIXED_RR", row)]));
    }

    private static ShadowResultRow Row(int day, string strategy, double net, double? gross, string basis = "net", string status = "graded") =>
        new(Start.AddDays(day), $"{strategy}|SYM{day}|Buy|d{day}", "primary", true, status, net, basis, basis == "net" ? "flat-10" : null, gross, 1, InSet: true);

    /// <summary>One candidate a session for <paramref name="count"/> sessions; net R <paramref name="net"/>, gross R <paramref name="net"/> + 0.12 (charges of 0.12 R).</summary>
    private void Series(int count, double net, string strategy = "ORB", string basis = "net") =>
        _reader.ResultsAsync(Ct).Returns([.. Enumerable.Range(0, count).Select(i => Row(i, strategy, net, basis == "net" ? net + 0.12 : net, basis))]);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a|b")]
    public async Task A_missing_or_malformed_strategy_is_refused(string? strategy)
    {
        var result = await RunAsync(strategy);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("paper.invalid");
    }

    [Fact]
    public async Task With_no_shadow_candidate_there_is_no_series_and_the_kill_rules_are_not_evaluated_rather_than_fine()
    {
        var view = (await RunAsync()).Value;

        view.HasSeries.ShouldBeFalse();
        view.Edge.State.ShouldBe("no_series");
        view.Kill.Select(k => k.Rule).ShouldBe(["cost_stress", "drawdown", "decay_twice", "failure_boundary"]);
        view.Kill.ShouldAllBe(k => k.State == "not_evaluated");
        view.Kill.Single(k => k.Rule == "failure_boundary").ProposalOnly.ShouldBeTrue();
    }

    [Fact]
    public async Task A_series_without_a_reference_is_unavailable_with_the_reason_and_is_not_judged()
    {
        Series(70, -1.0);

        var view = (await RunAsync()).Value;

        view.HasSeries.ShouldBeTrue();
        view.Reference.State.ShouldBe("unavailable");
        view.Edge.State.ShouldBe("unavailable");
        view.Edge.Windows.ShouldBeEmpty();
        view.Edge.Note!.ShouldContain("unavailable");
        view.Kill.Single(k => k.Rule == "decay_twice").State.ShouldBe("not_evaluated");
    }

    [Fact]
    public async Task A_reference_with_too_few_trades_or_no_spread_is_unavailable()
    {
        Series(70, -1.0);
        Reference(20);
        (await RunAsync()).Value.Reference.Reason!.ShouldContain("30");

        Reference(40, varied: false);
        (await RunAsync()).Value.Reference.State.ShouldBe("unavailable");
    }

    [Fact]
    public async Task A_net_series_against_a_net_reference_is_judged_window_by_window()
    {
        Reference(40);
        Series(130, -0.5);

        var view = (await RunAsync()).Value;

        view.Reference.ShouldBe(new DecayReferenceView("available", null, 40, view.Reference.MeanR, view.Reference.Sigma, "net"));
        view.Reference.MeanR!.Value.ShouldBe(0.12, 1e-9);
        view.Edge.State.ShouldBe("alert");
        view.Edge.Trades.ShouldBe(130);
        view.Edge.PendingTrades.ShouldBe(10);
        view.Edge.Windows.Count.ShouldBe(2);
        view.Edge.Windows.ShouldAllBe(w => w.Below && w.Trades == 60);
        view.Edge.TwiceInARow.ShouldBeTrue();
        view.Edge.Cusum!.Alarm.ShouldBeTrue();
        view.Edge.Quarters.Select(q => (q.Label, q.Weak)).ShouldBe([("2026-Q4", true), ("2027-Q1", true)]); // 130 sessions from 2 November cross the year end
        view.Edge.WeakQuarterShare.ShouldBe(1.0);
        view.Settings.SettingsHash.Length.ShouldBe(64);
        view.Line.ShouldContain("report only");
    }

    [Fact]
    public async Task The_kill_rules_are_evaluated_with_their_numbers_and_none_is_executed()
    {
        Reference(40);
        Series(130, -0.5);

        var kill = (await RunAsync()).Value.Kill.ToDictionary(k => k.Rule);

        kill["cost_stress"].State.ShouldBe("fires");
        kill["cost_stress"].Value!.Value.ShouldBe(-0.5 - 0.06, 1e-9);
        kill["cost_stress"].Trades.ShouldBe(100);
        kill["decay_twice"].State.ShouldBe("fires");
        kill["failure_boundary"].State.ShouldBe("fires");
        kill["failure_boundary"].ProposalOnly.ShouldBeTrue();
        kill["drawdown"].State.ShouldBe("not_evaluated");
        kill["drawdown"].Text.ShouldContain("no cell");
    }

    [Fact]
    public async Task A_healthy_series_alerts_nothing()
    {
        Reference(40);
        Series(130, 0.3);

        var view = (await RunAsync()).Value;

        view.Edge.State.ShouldBe("ok");
        view.Edge.Streak.ShouldBe(0);
        view.Kill.Single(k => k.Rule == "cost_stress").State.ShouldBe("holds");
        view.Kill.Single(k => k.Rule == "decay_twice").State.ShouldBe("holds");
        view.Kill.Single(k => k.Rule == "failure_boundary").State.ShouldBe("holds");
    }

    [Fact]
    public async Task Fewer_than_a_window_of_trades_reads_monitoring_and_cost_stress_is_not_evaluated_below_a_hundred()
    {
        Reference(40);
        Series(59, -3.0);

        var view = (await RunAsync()).Value;

        view.Edge.State.ShouldBe("monitoring");
        view.Edge.PendingTrades.ShouldBe(59);
        view.Kill.Single(k => k.Rule == "cost_stress").State.ShouldBe("not_evaluated");
        view.Kill.Single(k => k.Rule == "decay_twice").State.ShouldBe("not_evaluated");
    }

    [Fact]
    public async Task A_gross_series_is_not_compared_with_a_net_reference_and_has_no_charges_to_stress()
    {
        Reference(40);
        Series(130, -0.5, basis: "gross");
        _costs.ConfirmedOnAsync(Arg.Any<string>(), Ct).Returns(ValueTask.FromResult<DateOnly?>(null));

        var view = (await RunAsync()).Value;

        view.Edge.State.ShouldBe("unavailable");
        view.Edge.Note!.ShouldContain("net of costs but the series is gross");
        view.Kill.Single(k => k.Rule == "cost_stress").State.ShouldBe("not_evaluated");
    }

    [Fact]
    public async Task A_negative_cost_makes_cost_stress_not_evaluated()
    {
        Reference(40);
        _reader.ResultsAsync(Ct).Returns([.. Enumerable.Range(0, 130).Select(i => Row(i, "ORB", -0.5, i == 120 ? -0.51 : -0.38))]);

        var kill = (await RunAsync()).Value.Kill.Single(k => k.Rule == "cost_stress");

        kill.State.ShouldBe("not_evaluated");
        kill.Text.ShouldContain("negative cost");
    }

    [Fact]
    public async Task A_strategy_with_only_counted_paper_trades_has_a_series_to_monitor()
    {
        Reference(40);
        var rows = Enumerable.Range(1, 5).Select(i => new PaperCompareRow(i, i, "primary", "measured", "ORB|15m|BUY|intraday|FIXED_RR|*", $"ORB|SYM{i}|Buy|d{i}", T0.AddDays(i), T0.AddDays(i).AddHours(5), 0.1, "net", "flat-10")).ToList();
        _paper.ReadAsync(1, Ct).Returns(new PaperCompareData(rows, 0, null));

        var view = (await RunAsync()).Value;

        view.Edge.State.ShouldBe("no_series");
        view.Behavioural.Trades.ShouldBe(5);
        view.HasSeries.ShouldBeTrue();
    }

    [Fact]
    public async Task Only_the_named_strategy_is_in_the_series()
    {
        Reference(40);
        _reader.ResultsAsync(Ct).Returns([.. Enumerable.Range(0, 60).Select(i => Row(i, "ORB", -0.5, -0.38)), .. Enumerable.Range(60, 60).Select(i => Row(i, "VWAP", 5, 5.12))]);

        var view = (await RunAsync()).Value;

        view.Edge.Trades.ShouldBe(60);
    }

    [Fact]
    public async Task The_behavioural_series_of_the_strategy_is_judged_beside_the_edge_series()
    {
        Reference(40);
        Series(10, 0.3);
        var rows = Enumerable.Range(1, 60).Select(i => new PaperCompareRow(i, i, "primary", "measured", "ORB|15m|BUY|intraday|FIXED_RR|*", $"ORB|SYM{i}|Buy|d{i}", T0.AddDays(i), T0.AddDays(i).AddHours(5), -0.6, "net", "flat-10")).ToList();
        rows.Add(new PaperCompareRow(99, 99, "primary", "measured", "VWAP|15m|BUY|intraday|FIXED_RR|*", "VWAP|SYM|Buy|d", T0, T0.AddMinutes(2000), 9, "net", "flat-10"));
        _paper.ReadAsync(1, Ct).Returns(new PaperCompareData(rows, 0, null));

        var view = (await RunAsync()).Value;

        view.Behavioural.Trades.ShouldBe(60);
        view.Behavioural.State.ShouldBe("alert");
        view.Behavioural.Basis.ShouldContain("net R as the P10 count stores it");
        view.Edge.State.ShouldBe("monitoring");
    }

    [Fact]
    public async Task The_window_setting_changes_the_windows_and_the_hash()
    {
        Reference(40);
        Series(130, -0.5);

        var wide = (await RunAsync()).Value;
        var narrow = (await RunAsync(options: new DecayOptions(WindowTrades: 20))).Value;

        wide.Edge.Windows.Count.ShouldBe(2);
        narrow.Edge.Windows.Count.ShouldBe(6);
        narrow.Settings.SettingsHash.ShouldNotBe(wide.Settings.SettingsHash);
    }

    [Fact]
    public async Task Without_an_epoch_there_is_no_reference_and_no_series()
    {
        _ledger.EpochAsync(null, Ct).Returns((EpochRow?)null);

        var view = (await RunAsync()).Value;

        view.Epoch.ShouldBeNull();
        view.Reference.State.ShouldBe("unavailable");
        view.HasSeries.ShouldBeFalse();
        view.Behavioural.Note!.ShouldContain("No paper epoch");
    }
}
