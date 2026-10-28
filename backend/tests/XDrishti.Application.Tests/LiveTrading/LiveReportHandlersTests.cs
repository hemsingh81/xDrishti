using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.LiveTrading;

namespace XDrishti.Application.Tests.LiveTrading;

/// <summary>Spec P11-03 at the use-case level on fakes: the pair of criterion 1, every exclusion reason, the paper-account rule, gross/net, empty and unavailable stores, and the execution-gap block of the spec's fixture.</summary>
public sealed class LiveReportHandlersTests
{
    private static readonly XDrishti.Domain.Accounts.BrokerAccount LiveAccount = new LiveCaptureFixture().Account;
    private static readonly Guid Live = LiveAccount.Id;
    private static readonly Guid PaperA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid PaperB = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly DateOnly Day = new(2026, 11, 3);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private sealed class Settings(Guid? paper = null) : ILiveReportSettings
    {
        public double NonInferiorityR => 0.10;

        public int MinPairs => 30;

        public Guid? PaperAccountId => paper;
    }

    private static NewLiveExecution Exec(long item, decimal vwap = 1250.475m, decimal? exit = 1262m, long qty = 40, decimal? rho = 10m, decimal? charges = null, double? bps = 3.8, string kind = "ticket_level", decimal? planEntry = 1250m, decimal? planExit = 1264m) =>
        new(item, Live, item, Day, "RELIANCE", "buy", "delivery", "CNC", "L1", 640m, 0.0008m, 0.05m, kind, 1250m, rho, 1250.2m, vwap, qty, 0.475, 0.2, 0.275, 9.5, bps, 0.0475, 0.02, 0.0275, 70, planEntry, planExit, 1240m, 0.0, exit, exit is null ? null : qty, charges);

    private static LiveTrip Trip(long item, NewLiveExecution? e, string outcome = "filled", string strategy = "ORB", double? gradeR = 1.4, DateOnly? date = null) =>
        new(item, Live, date ?? Day, "RELIANCE", "buy", strategy, outcome, "graded", gradeR, e is null ? null : new StoredLiveExecution(item, e, DateTimeOffset.UnixEpoch));

    private static (GetLiveVsPaperHandler Handler, IPaperTradeSource Paper) VsPaper(IReadOnlyList<LiveTrip> trips, PaperAccountsRead accounts, PaperTradesRead? trades = null, Guid? configured = null)
    {
        var accountsStore = Substitute.For<IAccountStore>();
        accountsStore.FindReadOnlyAsync(Live, Arg.Any<CancellationToken>()).Returns(LiveAccount);
        var live = Substitute.For<ILivePairSource>();
        live.TripsAsync(Live, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(trips);
        var paper = Substitute.For<IPaperTradeSource>();
        paper.AccountsAsync(Arg.Any<CancellationToken>()).Returns(accounts);
        paper.TradesAsync(Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<long>>(), Arg.Any<CancellationToken>()).Returns(trades ?? new PaperTradesRead(true, [], new Dictionary<long, string>()));
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        return (new GetLiveVsPaperHandler(accountsStore, live, paper, new Settings(configured), time, NullLogger<GetLiveVsPaperHandler>.Instance), paper);
    }

    private static LiveVsPaperQuery Q(string? basis = null, string? kind = null, Guid? paper = null) => new(Live, paper, Day.AddDays(-10), Day.AddDays(5), basis, kind, 1, 200);

    private static PaperAccountsRead One => new(true, 7, Day, [PaperA]);

    private static PaperTrade P(long item, decimal entry = 1250.30m, decimal? exit = 1263m, decimal? charges = null) => new(item, "RELIANCE", "buy", entry, 40, exit, charges);

    [Fact]
    public async Task A_filled_pair_has_the_hand_worked_split_and_the_evidence()
    {
        var (h, _) = VsPaper([Trip(1, Exec(1))], One, new PaperTradesRead(true, [P(1)], new Dictionary<long, string>()));

        var view = (await h.HandleAsync(Q(), Ct)).Value;

        var pair = view.Pairs.Single();
        pair.ShortfallR.ShouldBe(0.1175, 1e-9);
        pair.EntryDiffR.ShouldBe(0.0175, 1e-9);
        pair.ExitDiffR.ShouldBe(0.1000, 1e-9);
        pair.PaperR.ShouldBe(1.2700, 1e-9);
        pair.LiveR.ShouldBe(1.1525, 1e-9);
        view.Summary.NPairs.ShouldBe(1);
        view.Summary.Mean.State.ShouldBe("insufficient");
        view.Summary.Mean.Number.ShouldBeNull();
        view.Summary.Verdict.ShouldBe("insufficient");
        view.Evidence.PaperAccountId.ShouldBe(PaperA);
        view.Evidence.PaperEpochId.ShouldBe(7);
        view.Evidence.Basis.ShouldBe("gross");
        view.Evidence.N.ShouldBe(1);
        view.Equity.Live.Points.Single().Value.ShouldBe(1.1525, 1e-9);
        view.Equity.PlanPrices.Points.Single().Value.ShouldBe(1.4, 1e-9);
        view.Histogram.State.ShouldBe("insufficient");
    }

    [Fact]
    public async Task Every_exclusion_reason_is_listed_once_and_counted()
    {
        var trips = new List<LiveTrip>
        {
            Trip(1, Exec(1)), // paired
            Trip(2, Exec(2)), // live_only (paper skipped)
            Trip(3, null, "missed_no_order"), // paper_only
            Trip(4, Exec(4, exit: null)), // live_open
            Trip(5, Exec(5)), // paper_open
            Trip(6, Exec(6, rho: null)), // no_plan_levels
            Trip(7, null, "not_triggered"), // neither side took it: not listed
        };
        var paper = new PaperTradesRead(true, [P(1), P(3), P(4), P(5, exit: null), P(6)], new Dictionary<long, string> { [2] = "gap_beyond_limit" });
        var (h, _) = VsPaper(trips, One, paper);

        var view = (await h.HandleAsync(Q(), Ct)).Value;

        view.Pairs.Select(p => p.PlanItemId).ShouldBe([1L]);
        view.Excluded.ToDictionary(e => e.PlanItemId, e => e.Reason).ShouldBe(new Dictionary<long, string> { [2] = "live_only", [3] = "paper_only", [4] = "live_open", [5] = "paper_open", [6] = "no_plan_levels" });
        view.Excluded.Single(e => e.PlanItemId == 2).Detail.ShouldContain("gap_beyond_limit");
        view.Excluded.Single(e => e.PlanItemId == 3).Detail.ShouldContain("missed_no_order");
        view.Evidence.Exclusions.Sum(e => e.Count).ShouldBe(5);
    }

    [Fact]
    public async Task Several_paper_accounts_need_a_choice_none_and_one_do_not()
    {
        var two = new PaperAccountsRead(true, 7, Day, [PaperA, PaperB]);

        var (h, _) = VsPaper([], two);
        var refused = await h.HandleAsync(Q(), Ct);
        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("live.paper_account_required");

        (await VsPaper([], two).Handler.HandleAsync(Q(paper: PaperB), Ct)).Value.Evidence.PaperAccountId.ShouldBe(PaperB);
        (await VsPaper([], two, configured: PaperA).Handler.HandleAsync(Q(), Ct)).Value.Evidence.PaperAccountId.ShouldBe(PaperA);
        (await VsPaper([], two).Handler.HandleAsync(Q(paper: Guid.NewGuid()), Ct)).Error.Code.ShouldBe("live.invalid");

        var none = (await VsPaper([Trip(1, Exec(1))], new PaperAccountsRead(true, null, null, [])).Handler.HandleAsync(Q(), Ct)).Value;
        none.Evidence.PaperAccountId.ShouldBeNull();
        none.Excluded.Single().Reason.ShouldBe("live_only");
    }

    [Fact]
    public async Task Net_is_used_only_when_both_sides_have_charges_for_every_pair_otherwise_the_report_is_gross()
    {
        var withCharges = VsPaper([Trip(1, Exec(1, charges: 40m))], One, new PaperTradesRead(true, [P(1, charges: 20m)], new Dictionary<long, string>()));
        var net = (await withCharges.Handler.HandleAsync(Q("net"), Ct)).Value;
        net.Evidence.Basis.ShouldBe("net");
        net.Pairs.Single().LiveR.ShouldBe(1.1525 - (40.0 / (10 * 40)), 1e-9);
        net.Pairs.Single().PaperR.ShouldBe(1.27 - (20.0 / (10 * 40)), 1e-9);
        net.Pairs.Single().ShortfallR.ShouldBe(0.1175 + 0.1 - 0.05, 1e-9);

        var missing = VsPaper([Trip(1, Exec(1, charges: 40m))], One, new PaperTradesRead(true, [P(1)], new Dictionary<long, string>()));
        var gross = (await missing.Handler.HandleAsync(Q("net"), Ct)).Value;
        gross.Evidence.Basis.ShouldBe("gross");
        gross.Pairs.Single().ShortfallR.ShouldBe(0.1175, 1e-9);
    }

    [Fact]
    public async Task Thirty_pairs_with_a_small_shortfall_are_within_and_an_unreadable_paper_store_is_unavailable_not_empty()
    {
        var trips = Enumerable.Range(1, 30).Select(i => Trip(i, Exec(i, vwap: 1250.30m + (i % 3 * 0.01m), exit: 1262.9m))).ToList();
        var paper = new PaperTradesRead(true, [.. Enumerable.Range(1, 30).Select(i => P(i))], new Dictionary<long, string>());
        var view = (await VsPaper(trips, One, paper).Handler.HandleAsync(Q(), Ct)).Value;
        view.Summary.NPairs.ShouldBe(30);
        view.Summary.Verdict.ShouldBe("within");
        view.Summary.Mean.State.ShouldBe("value");
        view.Summary.Hi95.Number.ShouldNotBeNull();

        var down = (await VsPaper(trips, new PaperAccountsRead(false, null, null, [])).Handler.HandleAsync(Q(), Ct)).Value;
        down.State.ShouldBe("unavailable");
        down.Summary.Mean.State.ShouldBe("unavailable");
        down.Pairs.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_store_is_empty_and_bad_inputs_are_refused()
    {
        (await VsPaper([], One).Handler.HandleAsync(Q(), Ct)).Value.State.ShouldBe("empty");
        (await VsPaper([], One).Handler.HandleAsync(Q("weird"), Ct)).Error.Code.ShouldBe("live.invalid");
        (await VsPaper([], One).Handler.HandleAsync(Q(kind: "other"), Ct)).Error.Code.ShouldBe("live.invalid");
        (await VsPaper([], One).Handler.HandleAsync(Q() with { From = Day.AddDays(-400), To = Day }, Ct)).Error.Code.ShouldBe("live.invalid");
        (await VsPaper([], One).Handler.HandleAsync(Q() with { AccountId = Guid.NewGuid() }, Ct)).Error.Code.ShouldBe("live.not_found");
    }

    [Fact]
    public async Task The_histogram_uses_one_reference_kind_and_the_same_data_gives_the_same_answer()
    {
        var trips = Enumerable.Range(1, 12).Select(i => Trip(i, Exec(i, bps: i - 4, kind: i <= 10 ? "ticket_level" : "plan_grade"))).ToList();
        var (h, _) = VsPaper(trips, One);

        var a = (await h.HandleAsync(Q(), Ct)).Value;
        var b = (await h.HandleAsync(Q(), Ct)).Value;

        a.Histogram.Kind.ShouldBe("ticket_level");
        a.Histogram.N.ShouldBe(10);
        a.Histogram.State.ShouldBe("value");
        a.Histogram.Bins.Sum(x => x.Count).ShouldBe(10);
        (await h.HandleAsync(Q(kind: "plan_grade"), Ct)).Value.Histogram.State.ShouldBe("insufficient"); // only 2 observations
        a.ShouldBe(b, new ViewComparer());
    }

    private sealed class ViewComparer : IEqualityComparer<LiveVsPaperView>
    {
        public bool Equals(LiveVsPaperView? x, LiveVsPaperView? y) => System.Text.Json.JsonSerializer.Serialize(x) == System.Text.Json.JsonSerializer.Serialize(y);

        public int GetHashCode(LiveVsPaperView obj) => 0;
    }

    private static GetExecutionGapHandler Gap(IReadOnlyList<LiveTrip> trips)
    {
        var accountsStore = Substitute.For<IAccountStore>();
        accountsStore.FindReadOnlyAsync(Live, Arg.Any<CancellationToken>()).Returns(LiveAccount);
        var live = Substitute.For<ILivePairSource>();
        live.TripsAsync(Live, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(trips);
        return new GetExecutionGapHandler(accountsStore, live, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)), NullLogger<GetExecutionGapHandler>.Instance);
    }

    [Fact]
    public async Task The_fixture_block_balances_and_the_missed_unknown_unknown_and_open_tickets_are_handled_apart()
    {
        // T1: plan entry 1250, exit 1264, stop 1240, fills 1250.475 -> 1262: plan R 1.4, entry 0.0475, exit 0.2
        var trips = new List<LiveTrip>
        {
            Trip(1, Exec(1)),
            Trip(3, null, "missed_no_order", gradeR: 1.2),
            Trip(2, null, "rejected", gradeR: 0.8),
            Trip(5, null, "missed_unknown", gradeR: 0.5),
            Trip(6, null, "unknown", gradeR: null),
            Trip(7, Exec(7, exit: null)),
            Trip(8, null, "not_triggered", gradeR: null),
            Trip(9, null, "unknown", gradeR: null, date: Day.AddDays(-5)), // left out: its date must not widen the evidence range
        };

        var view = (await Gap(trips).HandleAsync(new ExecutionGapQuery(Live, Day.AddDays(-10), Day.AddDays(5), "strategy"), Ct)).Value;

        var g = view.Groups.Single();
        g.Key.ShouldBe("ORB");
        g.N.ShouldBe(4);
        g.Excluded.ShouldBe(3);
        g.Balanced.ShouldBeTrue();
        double R(string label) => g.Rows.Single(r => r.Label == label).R.Number!.Value;
        R("Plan R").ShouldBe(3.9, 1e-9);
        R("Missed triggers").ShouldBe(-2.0, 1e-9);
        R("Missed (cause unknown)").ShouldBe(-0.5, 1e-9);
        R("Entry slippage").ShouldBe(-0.0475, 1e-9);
        R("Exit difference").ShouldBe(-0.2, 1e-9);
        R("Result").ShouldBe(1.1525, 1e-9); // gross: charges are not recorded for the trip
        g.Rows.Single(r => r.Label == "Charges difference").R.State.ShouldBe("unavailable");
        g.Rows.Single(r => r.Label == "Overrides").R.State.ShouldBe("unavailable");
        g.Basis.ShouldBe("gross");
        g.PerTicketMean.State.ShouldBe("insufficient");
        view.Excluded.Select(e => e.Reason).Order().ShouldBe(["live_open", "unknown", "unknown"]);
        (R("Plan R") + R("Missed triggers") + R("Missed (cause unknown)") + R("Entry slippage") + R("Exit difference")).ShouldBe(R("Result"), 1e-9);
        view.Total.N.ShouldBe(4);
        view.Evidence.N.ShouldBe(4);
        view.Evidence.FirstDate.ShouldBe(Day);
    }

    [Fact]
    public async Task Recorded_charges_give_a_net_result_and_groups_are_listed_in_key_order()
    {
        var trips = new List<LiveTrip>
        {
            Trip(1, Exec(1, charges: 38.40m), strategy: "ZZZ"),
            Trip(2, Exec(2, charges: 38.40m), strategy: "ABC"),
        };

        var view = (await Gap(trips).HandleAsync(new ExecutionGapQuery(Live, Day.AddDays(-10), Day.AddDays(5), "strategy"), Ct)).Value;

        view.Groups.Select(g => g.Key).ShouldBe(["ABC", "ZZZ"]);
        view.Groups[0].Basis.ShouldBe("net");
        view.Groups[0].Rows.Single(r => r.Label == "Charges difference").R.Number!.Value.ShouldBe(-0.096, 1e-9);
        view.Groups[0].Rows.Single(r => r.Label == "Result").R.Number!.Value.ShouldBe(1.4 - 0.0475 - 0.2 - 0.096, 1e-9);
        view.Total.Rows.Single(r => r.Label == "Plan R").R.Number!.Value.ShouldBe(2.8, 1e-9);

        var byMonth = (await Gap(trips).HandleAsync(new ExecutionGapQuery(Live, Day.AddDays(-10), Day.AddDays(5), "month"), Ct)).Value;
        byMonth.Groups.Single().Key.ShouldBe("2026-11");
        (await Gap(trips).HandleAsync(new ExecutionGapQuery(Live, null, null, "tag"), Ct)).Error.Code.ShouldBe("live.invalid");
        (await Gap([]).HandleAsync(new ExecutionGapQuery(Live, null, null, null), Ct)).Value.State.ShouldBe("empty");
    }
}
