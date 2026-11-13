using NSubstitute;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>CF-09 (ADR 0213) over fake ports: the series assembly (edge rows of the current epoch only, graded, in session then trade-key order, the design effect from the day's candidates), the scope, the latch, the epoch restart, the behavioural series beside it and the honest text.</summary>
public sealed class GetSequentialStatusHandlerTests
{
    private static readonly DateOnly Start = new(2026, 11, 2);

    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 4, 0, 0, TimeSpan.Zero);

    private readonly IShadowResultReader _reader = Substitute.For<IShadowResultReader>();

    private readonly IPaperLedgerData _ledger = Substitute.For<IPaperLedgerData>();

    private readonly IBacktestPaperData _paper = Substitute.For<IBacktestPaperData>();

    private readonly ICostConfirmationFact _costs = Substitute.For<ICostConfirmationFact>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GetSequentialStatusHandlerTests()
    {
        Epoch(1);
        _paper.ReadAsync(Arg.Any<long>(), Ct).Returns(new PaperCompareData([], 0, null));
    }

    private void Epoch(long id) => _ledger.EpochAsync(null, Ct).Returns(new EpochRow(id, Start, DateTimeOffset.UnixEpoch, "first", "default", "system"));

    private static ShadowResultRow Row(int day, string key, double? r, string status = "graded", long? epoch = 1, bool edge = true) =>
        new(Start.AddDays(day), key, "primary", edge, status, r, r is null ? null : "gross", null, r, epoch, InSet: true);

    /// <summary>One candidate a session (design effect 1), R given in order.</summary>
    private void Series(params double[] rs) => _reader.ResultsAsync(Ct).Returns([.. rs.Select((r, i) => Row(i, $"s|SYM{i}|Buy|d", r))]);

    private ValueTask<XDrishti.Domain.Common.Result<SequentialStatusView>> RunAsync(string scope = "system", string? strategy = null, SequentialOptions? options = null) =>
        new GetSequentialStatusHandler(_reader, _ledger, _paper, _costs, options ?? new SequentialOptions()).HandleAsync(new GetSequentialStatusQuery(scope, strategy), Ct);

    [Fact]
    public async Task Thirty_one_straight_losses_read_failure_with_the_day_of_the_first_crossings()
    {
        Series([.. Enumerable.Repeat(-1.0, 31)]);

        var view = (await RunAsync()).Value;

        view.Edge.State.ShouldBe("failure");
        view.Edge.TradesCounted.ShouldBe(31);
        (view.Edge.FirstCautionTrade, view.Edge.FirstCautionDate).ShouldBe((16, Start.AddDays(15)));
        (view.Edge.FirstFailureTrade, view.Edge.FirstFailureDate).ShouldBe((31, Start.AddDays(30)));
        view.Edge.Sum.ShouldBe(-3.031111, 5e-7);
        view.Boundaries.Failure.ShouldBe(-2.944439, 5e-7);
        view.HasSeries.ShouldBeTrue();
    }

    [Fact]
    public async Task A_failure_stays_latched_after_the_series_recovers()
    {
        Series([.. Enumerable.Repeat(-1.0, 31), .. Enumerable.Repeat(2.0, 150)]);

        var view = (await RunAsync()).Value;

        view.Edge.State.ShouldBe("failure");
        view.Edge.Sum.ShouldBeGreaterThan(view.Boundaries.Failure);
    }

    [Fact]
    public async Task An_epoch_restart_starts_an_empty_series_that_reads_monitoring()
    {
        Series([.. Enumerable.Repeat(-1.0, 31)]);
        Epoch(2); // the P10-05 restart: the rows were written in epoch 1

        var view = (await RunAsync()).Value;

        (view.Edge.State, view.Edge.TradesCounted, view.Epoch, view.HasSeries).ShouldBe(("monitoring", 0, 2L, false));
        view.Edge.Note.ShouldNotBeNull();
    }

    [Fact]
    public async Task Rows_that_do_not_count_for_edge_or_are_not_graded_are_not_in_the_series()
    {
        _reader.ResultsAsync(Ct).Returns([Row(0, "a", -1), Row(1, "b", -1, edge: false), Row(2, "c", null, "pending"), Row(3, "d", null, "not_triggered"), Row(4, "e", -1, epoch: null)]);

        var view = (await RunAsync()).Value;

        view.Edge.TradesCounted.ShouldBe(1);
        view.HasSeries.ShouldBeTrue();
    }

    [Fact]
    public async Task The_order_is_session_then_trade_key_and_the_design_effect_comes_from_the_days_candidates()
    {
        // day 0: two candidates (design effect 1.3), keys b then a; one graded loss, one graded win; the order must not depend on the stored order
        _reader.ResultsAsync(Ct).Returns([Row(0, "b", 2.0), Row(0, "a", -1.0), Row(1, "c", -1.0)]);

        var view = (await RunAsync()).Value;

        // a (-1, m 2), b (+2, m 2), c (-1, m 1)
        var expected = (SequentialFailureTest.Llr(-1, 0.2, 1.5) / 1.3) + (SequentialFailureTest.Llr(2, 0.2, 1.5) / 1.3) + SequentialFailureTest.Llr(-1, 0.2, 1.5);
        view.Edge.Sum.ShouldBe(expected, 1e-12);
        view.Edge.EffectiveN.ShouldBe((2 / 1.3) + 1, 1e-12);
        (view.Rho, view.RhoIsPlaceholder).ShouldBe((0.3, true));
    }

    [Fact]
    public async Task The_strategy_scope_reads_only_that_strategys_trades_and_needs_the_id()
    {
        _reader.ResultsAsync(Ct).Returns([Row(0, "alpha|X|Buy|d", -1), Row(1, "alpha2|X|Buy|d", -1), Row(2, "beta|X|Buy|d", -1)]);

        var one = (await RunAsync("strategy", "alpha")).Value;
        var missing = await RunAsync("strategy");
        var odd = await RunAsync("everything");
        var extra = await RunAsync("system", "alpha");

        (one.Scope, one.Strategy, one.Edge.TradesCounted).ShouldBe(("strategy", "alpha", 1));
        missing.IsFailure.ShouldBeTrue();
        odd.IsFailure.ShouldBeTrue();
        extra.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task With_no_rows_there_is_no_series_and_the_state_is_monitoring_not_a_pass()
    {
        _reader.ResultsAsync(Ct).Returns([]);

        var view = (await RunAsync()).Value;

        (view.HasSeries, view.Edge.State, view.Edge.TradesCounted).ShouldBe((false, "monitoring", 0));
    }

    [Fact]
    public async Task The_edge_series_uses_the_gross_r_until_the_charge_sheet_is_confirmed_and_says_so()
    {
        _reader.ResultsAsync(Ct).Returns([new ShadowResultRow(Start, "k", "primary", true, "graded", 0.8, "net", "flat-10", 1.0, 1L, InSet: true)]);
        _costs.ConfirmedOnAsync("flat-10", Ct).Returns((DateOnly?)null);

        var unconfirmed = (await RunAsync()).Value;
        _costs.ConfirmedOnAsync("flat-10", Ct).Returns(Start);
        var confirmed = (await RunAsync()).Value;

        unconfirmed.Edge.Basis.ShouldContain("gross R");
        unconfirmed.Edge.Sum.ShouldBe(SequentialFailureTest.Llr(1.0, 0.2, 1.5), 1e-12);
        confirmed.Edge.Basis.ShouldContain("net R");
        confirmed.Edge.Sum.ShouldBe(SequentialFailureTest.Llr(0.8, 0.2, 1.5), 1e-12);
    }

    [Fact]
    public async Task The_behavioural_series_of_P10_sits_beside_the_edge_series_in_exit_order()
    {
        Series(-1, -1);
        var rows = Enumerable.Range(1, 16).Select(i => new PaperCompareRow(i, i, "primary", "measured", "cell", "alpha|S" + i + "|Buy|d", T0.AddDays(i), T0.AddDays(i).AddHours(2), -1.0, "net", "flat-10")).ToList();
        _paper.ReadAsync(1, Ct).Returns(new PaperCompareData(rows, 0, null));

        var view = (await RunAsync()).Value;

        view.Behavioural.TradesCounted.ShouldBe(16);
        view.Behavioural.State.ShouldBe("caution"); // 16 losses of -1 R
        view.Behavioural.Basis.ShouldContain("net R");
        view.Edge.State.ShouldBe("monitoring"); // beside it, never merged
        view.Edge.TradesCounted.ShouldBe(2);
    }

    [Fact]
    public async Task With_no_paper_epoch_the_behavioural_series_is_empty_and_says_so()
    {
        Series(-1);
        _ledger.EpochAsync(null, Ct).Returns((EpochRow?)null);

        var view = (await RunAsync()).Value;

        (view.Epoch, view.Behavioural.TradesCounted, view.Behavioural.Note).ShouldBe((null, 0, "No paper epoch yet."));
    }

    [Fact]
    public async Task Every_answer_carries_the_settings_hash_the_honest_statements_and_a_line_that_is_not_a_pass()
    {
        Series(1);

        var view = (await RunAsync()).Value;

        view.SettingsHash.ShouldBe(new SequentialOptions().SnapshotHash());
        view.Statements.ShouldContain(s => s.Contains("Gaussian", StringComparison.Ordinal));
        view.Statements.ShouldContain(s => s.Contains("4.75 to 1", StringComparison.Ordinal) && s.Contains("19 to 1", StringComparison.Ordinal));
        view.Line.ShouldContain("report only, never a pass");
        view.Line.ShouldContain("fixed P10 gates decide");
        view.Line.ShouldContain(view.SettingsHash[..12]);
    }

    [Fact]
    public async Task A_changed_setting_changes_the_hash_and_can_clear_a_flag()
    {
        Series([.. Enumerable.Repeat(-1.0, 31)]);

        var stricter = (await RunAsync(options: new SequentialOptions(Mu1: 0.1))).Value;

        stricter.SettingsHash.ShouldNotBe(new SequentialOptions().SnapshotHash());
        stricter.Edge.State.ShouldNotBe("failure"); // mu1 0.1: the indifference point is 0.05 and the steps are smaller
    }

    [Fact]
    public async Task The_handler_asks_only_to_read()
    {
        Series(1);

        await RunAsync();

        await _reader.DidNotReceive().ChainAsync(Arg.Any<CancellationToken>());
        await _ledger.DidNotReceive().TradesAsync(Arg.Any<long>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failure_seen_stays_seen_when_a_later_pending_row_is_graded_as_a_win()
    {
        var losses = Enumerable.Range(0, 31).Select(i => Row(i, $"s|SYM{i}|Buy|d", -1.0));
        _reader.ResultsAsync(Ct).Returns([.. losses, Row(31, "s|LATE|Buy|d", null, "pending")]);
        var before = (await RunAsync()).Value;

        _reader.ResultsAsync(Ct).Returns([.. losses, Row(31, "s|LATE|Buy|d", 2.0)]);
        var after = (await RunAsync()).Value;

        before.Edge.State.ShouldBe("failure");
        after.Edge.State.ShouldBe("failure");
        after.Edge.FirstFailureTrade.ShouldBe(31);
        after.Edge.TradesCounted.ShouldBe(32);
    }

    [Fact]
    public async Task The_series_stops_at_the_first_session_with_a_pending_row_and_says_how_many_trades_are_held_back()
    {
        // session 0 still has an ungraded candidate: the 31 later losses are held back, so a late win in session 0 cannot rewrite a path that was shown
        var later = Enumerable.Range(1, 31).Select(i => Row(i, $"s|SYM{i}|Buy|d", -1.0));
        _reader.ResultsAsync(Ct).Returns([Row(0, "s|FIRST|Buy|d", null, "pending"), .. later]);

        var held = (await RunAsync()).Value.Edge;
        _reader.ResultsAsync(Ct).Returns([Row(0, "s|FIRST|Buy|d", 2.0), .. later]);
        var released = (await RunAsync()).Value.Edge;

        (held.State, held.TradesCounted, held.HeldBackTrades).ShouldBe(("monitoring", 0, 31));
        held.Note.ShouldNotBeNull().ShouldContain("2026-11-02");
        held.Note.ShouldContain("31 graded trade(s)");
        (released.TradesCounted, released.HeldBackTrades, released.Note).ShouldBe((32, 0, null));
        released.State.ShouldBe("caution"); // the +2 first and 31 losses: -2.86, above the failure boundary; read in the old way (31 losses first) it would have been a failure
        held.HeldBackFromSession.ShouldBe(Start);
        released.HeldBackFromSession.ShouldBeNull();
    }

    [Fact]
    public async Task The_line_shows_a_frozen_series_with_the_stuck_session_and_the_held_back_count_even_with_no_trade_counted()
    {
        var later = Enumerable.Range(1, 31).Select(i => Row(i, $"s|SYM{i}|Buy|d", -1.0));
        _reader.ResultsAsync(Ct).Returns([Row(0, "s|FIRST|Buy|d", null, "pending"), .. later]);

        var frozen = (await RunAsync()).Value;
        _reader.ResultsAsync(Ct).Returns([Row(0, "s|FIRST|Buy|d", -1.0), Row(1, "s|X|Buy|d", -1.0), Row(2, "s|LATE|Buy|d", null, "pending"), Row(3, "s|LATER|Buy|d", -1.0)]);
        var partial = (await RunAsync()).Value;

        frozen.Edge.TradesCounted.ShouldBe(0);
        frozen.Line.ShouldContain("monitoring, no trade in the series");
        frozen.Line.ShouldContain("series stops before 2026-11-02");
        frozen.Line.ShouldContain("may never get one");
        frozen.Line.ShouldContain("31 graded trade(s) held back");
        partial.Line.ShouldContain("2 trade(s)");
        partial.Line.ShouldContain("series stops before 2026-11-04");
        partial.Line.ShouldContain("1 graded trade(s) held back");
    }

    [Fact]
    public async Task A_complete_series_has_no_held_back_clause_in_the_line()
    {
        Series(-1, -1);

        (await RunAsync()).Value.Line.ShouldNotContain("held back");
    }

    [Fact]
    public async Task A_graded_trade_with_no_gross_figure_is_left_out_and_the_note_says_so()
    {
        _reader.ResultsAsync(Ct).Returns([new ShadowResultRow(Start, "k", "primary", true, "graded", 0.8, "net", "flat-10", null, 1L, InSet: true), Row(1, "g", -1.0)]);
        _costs.ConfirmedOnAsync("flat-10", Ct).Returns((DateOnly?)null);

        var edge = (await RunAsync()).Value.Edge;

        edge.TradesCounted.ShouldBe(1);
        edge.Note.ShouldNotBeNull().ShouldContain("1 graded trade(s) have no gross figure");
        edge.Note.ShouldContain("still count among the day's candidates");
    }

    [Fact]
    public async Task A_strategy_id_with_a_pipe_is_refused()
    {
        Series(-1);

        var result = await RunAsync("strategy", "a|b");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("paper.invalid");
    }
}
