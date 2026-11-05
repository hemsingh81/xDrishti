using NSubstitute;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>CF-08 part B, criteria 8, 9 and 10 over fake ports: the hand numbers, the pace, the groups, the two counts side by side, the gross label while the sheet is unconfirmed, and the honest line when there are no rows.</summary>
public sealed class GetShadowSummaryHandlerTests
{
    private static readonly DateOnly Start = new(2026, 11, 2);

    private readonly IShadowSettings _switch = Substitute.For<IShadowSettings>();

    private readonly IShadowResultReader _reader = Substitute.For<IShadowResultReader>();

    private readonly IPaperLedgerData _ledger = Substitute.For<IPaperLedgerData>();

    private readonly IBacktestPaperData _paper = Substitute.For<IBacktestPaperData>();

    private readonly ICostConfirmationFact _costs = Substitute.For<ICostConfirmationFact>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GetShadowSummaryHandlerTests()
    {
        _switch.IsEnabledAsync(Ct).Returns(true);
        _ledger.EpochAsync(null, Ct).Returns(new EpochRow(1, Start, DateTimeOffset.UnixEpoch, "first", "default", "system"));
        _paper.ReadAsync(1, Ct).Returns(new PaperCompareData([], 0, null));
        _reader.ChainAsync(Ct).Returns(new ShadowChainTotals(3, 1, Start.AddDays(2), new string('e', 64)));
    }

    /// <summary>Each of <paramref name="sessions"/> sessions has <paramref name="perSession"/> candidates that count for edge, all graded, with R alternating +1 and -0.5 (primary, reserve, rejected in turn). A net row also carries a gross R 0.2 above its net one.</summary>
    private void Candidates(int sessions, int perSession, string basis = "gross", string? sheet = null)
    {
        var groups = new[] { "primary", "reserve", "rejected" };
        var rows = new List<ShadowResultRow>();
        for (var s = 0; s < sessions; s++)
        {
            for (var c = 0; c < perSession; c++)
            {
                var r = c % 2 == 0 ? 1.0 : -0.5;
                rows.Add(new ShadowResultRow(Start.AddDays(s), $"k{s}-{c}", groups[c % 3], true, "graded", r, basis, sheet, basis == "net" ? r + 0.2 : r));
            }
        }

        _reader.ResultsAsync(Ct).Returns(rows);
    }

    private Task<ShadowSummaryView> ReadAsync() => new GetShadowSummaryHandler(_switch, _reader, _ledger, _paper, _costs).HandleAsync(new GetShadowSummaryQuery(), Ct).AsTask();

    [Fact]
    public async Task Raw_240_over_40_sessions_of_6_candidates_is_96_effective_with_the_placeholder_rho_said_so()
    {
        Candidates(40, 6);

        var view = await ReadAsync();

        view.Counts.EdgeRaw.ShouldBe(240);
        view.Edge.EffectiveN.ShouldBe(96.0, 1e-9);
        (view.Edge.Rho, view.Edge.RhoIsPlaceholder).ShouldBe((0.3, true));
        view.Edge.RhoNote.ShouldBe(ShadowWording.PlaceholderRho);
        view.Edge.EtaSessions.ShouldBe(0); // 96 effective is past the 60 aimed at
        view.Statement.ShouldBe("modelled fills; no queue, no latency");
        view.SeparateCount.ShouldBe(ShadowWording.SeparateCount);
    }

    [Fact]
    public async Task The_pace_is_the_observed_rate_once_ten_trades_are_graded_and_none_before()
    {
        Candidates(10, 6); // 60 raw, 24 effective, 2.4 a session: 36 more at 2.4 is 15 sessions

        var ten = await ReadAsync();
        Candidates(1, 6);
        var six = await ReadAsync();

        ten.Edge.EtaSessions.ShouldBe(15);
        six.Edge.EtaSessions.ShouldBeNull();
        six.Edge.EtaNote.ShouldContain("at least 10");
    }

    [Fact]
    public async Task The_mean_with_its_interval_and_the_hit_rate_are_given_overall_and_by_group()
    {
        Candidates(10, 6); // primary: c = 0, 3 -> +1, -0.5; reserve: c = 1, 4 -> -0.5, +1; rejected: c = 2, 5 -> +1, -0.5

        var view = await ReadAsync();

        view.Groups.Select(g => g.Group).ShouldBe(["all", "primary", "reserve", "rejected"]);
        var all = view.Groups[0];
        all.N.ShouldBe(60);
        all.MeanR.State.ShouldBe("value");
        all.MeanR.Number!.Value.ShouldBe(0.25, 1e-9);
        all.HitRate.Number!.Value.ShouldBe(0.5, 1e-9);
        all.Low90.Number!.Value.ShouldBeLessThan(0.25);
        all.High90.Number!.Value.ShouldBeGreaterThan(0.25);
        view.Groups.Skip(1).ShouldAllBe(g => g.N == 20);
    }

    [Fact]
    public async Task Fewer_than_ten_trades_give_a_provisional_mean_and_no_interval()
    {
        Candidates(1, 6);

        var all = (await ReadAsync()).Groups[0];

        (all.MeanR.State, all.Low90.State, all.High90.State).ShouldBe(("provisional", "insufficient", "insufficient"));
        all.Low90.Number.ShouldBeNull();
    }

    [Fact]
    public async Task The_same_rows_give_the_same_interval()
    {
        Candidates(10, 6);

        var a = await ReadAsync();
        var b = await ReadAsync();

        a.Groups[0].Low90.Number.ShouldBe(b.Groups[0].Low90.Number);
    }

    [Fact]
    public async Task With_no_rows_it_says_so_and_why_nothing_is_written_while_the_switch_is_off()
    {
        _switch.IsEnabledAsync(Ct).Returns(false);
        _reader.ResultsAsync(Ct).Returns([]);

        var off = await ReadAsync();
        _switch.IsEnabledAsync(Ct).Returns(true);
        var on = await ReadAsync();

        off.HasRows.ShouldBeFalse();
        off.Note.ShouldNotBeNull().ShouldContain("OFF");
        on.Note.ShouldNotBeNull().ShouldContain("No shadow row is stored yet");
        off.Groups[0].MeanR.State.ShouldBe("unavailable");
        off.Statement.ShouldBe("modelled fills; no queue, no latency");
    }

    [Fact]
    public async Task While_the_sheet_is_not_confirmed_the_numbers_are_the_gross_r_and_the_label_the_flag_and_the_figures_agree()
    {
        Candidates(10, 6, "net", "flat-10"); // net R: +1 and -0.5 (mean 0.25); gross R of the same trades: +1.2 and -0.3 (mean 0.45)
        _costs.ConfirmedOnAsync("flat-10", Ct).Returns((DateOnly?)null);

        var view = await ReadAsync();

        view.Costs.Gross.ShouldBeTrue();
        view.Costs.Confirmed.ShouldBeFalse();
        view.Costs.Basis.ShouldStartWith("gross, before charges");
        view.Costs.Basis.ShouldContain("not confirmed");
        view.Groups[0].MeanR.Number!.Value.ShouldBe(0.45, 1e-9);
        view.Groups[0].HitRate.Number!.Value.ShouldBe(0.5, 1e-9);
        view.Groups[0].Low90.Number!.Value.ShouldBeLessThan(0.45);
        view.Groups[0].High90.Number!.Value.ShouldBeGreaterThan(0.45);
    }

    [Fact]
    public async Task When_every_sheet_is_confirmed_the_numbers_are_the_net_r_and_the_label_calls_them_net()
    {
        Candidates(10, 6, "net", "flat-10");
        _costs.ConfirmedOnAsync("flat-10", Ct).Returns(Start);

        var view = await ReadAsync();

        (view.Costs.Gross, view.Costs.Confirmed).ShouldBe((false, true));
        view.Costs.Basis.ShouldStartWith("net of the charge sheet flat-10");
        view.Groups[0].MeanR.Number!.Value.ShouldBe(0.25, 1e-9);
    }

    [Fact]
    public async Task A_gross_result_says_no_charges_and_its_number_is_the_gross_r()
    {
        Candidates(2, 6);

        var view = await ReadAsync();

        (view.Costs.Gross, view.Costs.Basis).ShouldBe((true, "gross, no charges"));
        view.Groups[0].MeanR.Number!.Value.ShouldBe(0.25, 1e-9);
    }

    [Fact]
    public async Task A_mix_of_net_and_gross_trades_shows_the_gross_r_of_each_and_says_so()
    {
        _reader.ResultsAsync(Ct).Returns(
        [
            new ShadowResultRow(Start, "a", "primary", true, "graded", 0.8, "net", "flat-10", 1.0),
            new ShadowResultRow(Start, "b", "primary", true, "graded", 2.0, "gross", null, 2.0),
        ]);
        _costs.ConfirmedOnAsync("flat-10", Ct).Returns(Start);

        var view = await ReadAsync();

        view.Costs.Gross.ShouldBeTrue();
        view.Costs.Basis.ShouldContain("some trades carry");
        view.Groups[0].MeanR.Number!.Value.ShouldBe(1.5, 1e-9); // (1.0 + 2.0) / 2, never the net 0.8
    }

    [Fact]
    public async Task The_pace_counts_only_sessions_with_a_final_grade_so_ungraded_recent_sessions_do_not_lower_the_rate()
    {
        Candidates(10, 6); // 60 graded in 10 sessions: 2.4 a session, 15 more sessions to 60 effective
        var rows = (await _reader.ResultsAsync(Ct)).ToList();
        for (var s = 10; s < 15; s++)
        {
            rows.AddRange(Enumerable.Range(0, 6).Select(c => new ShadowResultRow(Start.AddDays(s), $"p{s}-{c}", "primary", true, "pending", null, null, null, null)));
        }

        _reader.ResultsAsync(Ct).Returns(rows);

        var view = await ReadAsync();

        view.Edge.Sessions.ShouldBe(10);
        view.Edge.EtaSessions.ShouldBe(15); // five sessions not graded yet are not slow sessions
        view.Counts.Pending.ShouldBe(30);
    }

    [Fact]
    public async Task The_behavioural_count_is_the_P10_counted_set_beside_the_edge_count_and_never_added_to_it()
    {
        Candidates(2, 6);
        var t0 = new DateTimeOffset(2026, 11, 3, 4, 0, 0, TimeSpan.Zero);
        var primary = new PaperCompareRow(1, 1, "primary", "measured", "cell", "k1", t0, t0.AddHours(2), 0.5, "gross", null);
        var reserve = new PaperCompareRow(2, 2, "reserve", "measured", "cell", "k2", t0, t0.AddHours(2), 0.5, "gross", null);
        _paper.ReadAsync(1, Ct).Returns(new PaperCompareData([primary, reserve], 0, null));

        var view = await ReadAsync();

        view.Counts.Behavioural.ShouldBe(1); // the reserve ticket is shown, not counted
        view.Counts.EdgeRaw.ShouldBe(12);
        view.Counts.Candidates.ShouldBe(12);
    }

    [Fact]
    public async Task Candidates_that_are_not_graded_yet_or_set_aside_are_counted_apart_and_not_in_n()
    {
        _reader.ResultsAsync(Ct).Returns(
        [
            new ShadowResultRow(Start, "a", "primary", true, "graded", 1.0, "gross", null, 1.0),
            new ShadowResultRow(Start, "b", "reserve", true, "pending", null, null, null, null),
            new ShadowResultRow(Start, "c", "reserve", true, "not_triggered", null, null, null, null),
            new ShadowResultRow(Start, "d", "rejected", true, "not_gradable", null, null, null, null),
            new ShadowResultRow(Start, "e", "rejected", false, "graded", 5.0, "gross", null, 5.0),
        ]);

        var view = await ReadAsync();

        view.Counts.ShouldBe(new ShadowCountsView(0, 1, view.Counts.EdgeEffective, 5, 4, 1, 1, 1));
        view.Groups[0].N.ShouldBe(1); // the set-aside graded row is not edge evidence
        view.Edge.EffectiveN.ShouldBe(1.0 / ShadowEdgeDesign(4), 1e-9); // one graded of four candidates that day
    }

    private static double ShadowEdgeDesign(int m) => EdgeCount.DesignEffect(m, EdgeCount.PlaceholderRho);

    [Fact]
    public async Task The_chain_head_is_shown()
    {
        _reader.ResultsAsync(Ct).Returns([]);

        var view = await ReadAsync();

        view.Chain.ShouldBe(new ShadowChainView(3, 1, Start.AddDays(2), new string('e', 64)));
    }
}
