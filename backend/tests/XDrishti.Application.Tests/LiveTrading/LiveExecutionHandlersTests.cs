using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.LiveTrading;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.LiveTrading;

/// <summary>Spec P11-02 at the use-case level on fakes: the fixture day measured (criteria 1, 2, 6), idempotent, no order evidence, estimates idempotent per week, the proposal gate, and the read views (criterion 11).</summary>
public sealed class LiveExecutionHandlersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Rig
    {
        public Rig(bool orderBook = true)
        {
            Fixture = new LiveCaptureFixture { OrderBookOn = orderBook };
            Store = new InMemoryExecutionStore();
            Market = new FakeLiveMarket();
            Settings = new FakeEstimateSettings();
        }

        public LiveCaptureFixture Fixture { get; }
        public InMemoryExecutionStore Store { get; }
        public FakeLiveMarket Market { get; }
        public FakeEstimateSettings Settings { get; }

        public ComputeLiveExecutionsHandler Compute() => new(Fixture.Accounts, Fixture.Store, Store, Market, new LiveTicketSource(Fixture.Plans), Fixture.Grades, Settings, Fixture.Calendar, Fixture.Time, NullLogger<ComputeLiveExecutionsHandler>.Instance);

        public ComputeLiveEstimatesHandler Estimates() => new(Store, Settings, Fixture.Time, NullLogger<ComputeLiveEstimatesHandler>.Instance);

        public async Task SettledAsync()
        {
            await Fixture.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);
            await Fixture.Settle().HandleAsync(new SettleLiveSessionCommand(LiveCaptureFixture.Day, null), Ct);
        }

        /// <summary>T1's plan grade with its simulated prices (entry 1,250, exit 1,264, stop 1,240), gross.</summary>
        public void GradeT1WithPrices() =>
            Fixture.StoredGrades[1] = new StoredGrade(1, new NewGrade(1, 1, LiveCaptureFixture.Day, new TicketGrade(GradeStatus.Graded, 1.4, null, null, "target", 1250, 1264, "gross, no charges", "test", 1240), null, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task The_fixture_day_gives_t1_its_execution_with_the_hand_worked_numbers()
    {
        var rig = new Rig();
        await rig.SettledAsync();
        rig.GradeT1WithPrices();
        foreach (var fill in rig.Fixture.Store.Fills)
        {
            rig.Fixture.Store.Charges[fill.Id] = fill.BrokerTradeId == "T-3" ? 20.40m : 9m; // 9 + 9 + 20.40 = 38.40 on the three T1 fills
        }

        var result = await rig.Compute().HandleAsync(new ComputeLiveExecutionsCommand(LiveCaptureFixture.Day, null), Ct);

        result.IsSuccess.ShouldBeTrue();
        var e = rig.Store.Executions.Single().Row;
        e.ItemId.ShouldBe(1);
        e.Symbol.ShouldBe("RELIANCE");
        e.ReferenceKind.ShouldBe(ReferenceKind.TicketLevel);
        e.ReferencePrice.ShouldBe(1250m);
        e.FillQty.ShouldBe(40);
        e.FillVwap.ShouldBe(1250.475m);
        e.ArrivalPrice.ShouldBe(1250.20m);
        e.SlipTotal.ShouldNotBeNull().ShouldBe(0.475, 1e-9);
        e.SlipDelay.ShouldNotBeNull().ShouldBe(0.20, 1e-9);
        e.SlipExec.ShouldNotBeNull().ShouldBe(0.275, 1e-9);
        e.SlipTotalTicks.ShouldNotBeNull().ShouldBe(9.5, 1e-9);
        e.SlipTotalBps.ShouldNotBeNull().ShouldBe(3.8, 1e-9);
        e.SlipTotalR.ShouldNotBeNull().ShouldBe(0.0475, 1e-9);
        e.Bucket.ShouldBe("L1");
        e.AdvTurnoverCr.ShouldBe(640m);
        e.ParticipationPct.ShouldBe(0.000782m); // 50,019 / 6.4e9 x 100
        e.Style.ShouldBe("delivery");
        e.EntryDelaySeconds.ShouldBe(70); // order created 09:40:00, first fill 09:41:10
        e.ExitVwap.ShouldBe(1262m);
        e.ExitQty.ShouldBe(40);
        e.LiveCharges.ShouldBe(38.40m);
        result.Value.ExecutionsWritten.ShouldBe(1);
    }

    [Fact]
    public async Task The_waterfall_view_of_the_fixture_trip_adds_up_to_the_spec_rows()
    {
        var rig = new Rig();
        await rig.SettledAsync();
        rig.GradeT1WithPrices();
        foreach (var fill in rig.Fixture.Store.Fills)
        {
            rig.Fixture.Store.Charges[fill.Id] = fill.BrokerTradeId == "T-3" ? 20.40m : 9m;
        }

        await rig.Compute().HandleAsync(new ComputeLiveExecutionsCommand(LiveCaptureFixture.Day, null), Ct);
        var view = (await new GetLiveWaterfallHandler(rig.Store).HandleAsync(new LiveWaterfallQuery(1), Ct)).Value;

        view.State.ShouldBe("value");
        view.Basis.ShouldBe("net");
        view.Balanced.ShouldBeTrue();
        view.Rows.Select(r => r.R.Number!.Value).ToArray().ShouldBe([1.4, 0.0475, 0.2, 1.1525, 0.096, 1.0565], new DoubleComparer());
    }

    private sealed class DoubleComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 1e-9;

        public int GetHashCode(double obj) => 0;
    }

    [Fact]
    public async Task A_trip_with_no_recorded_charges_is_gross_and_a_plan_without_prices_has_no_waterfall()
    {
        var rig = new Rig();
        await rig.SettledAsync();
        rig.GradeT1WithPrices();
        await rig.Compute().HandleAsync(new ComputeLiveExecutionsCommand(LiveCaptureFixture.Day, null), Ct);

        var gross = (await new GetLiveWaterfallHandler(rig.Store).HandleAsync(new LiveWaterfallQuery(1), Ct)).Value;
        gross.Basis.ShouldBe("gross");
        gross.Rows.Count.ShouldBe(4);

        var bare = new Rig();
        await bare.SettledAsync(); // the grade has no prices
        await bare.Compute().HandleAsync(new ComputeLiveExecutionsCommand(LiveCaptureFixture.Day, null), Ct);
        var none = (await new GetLiveWaterfallHandler(bare.Store).HandleAsync(new LiveWaterfallQuery(1), Ct)).Value;
        none.State.ShouldBe("unavailable");
        none.Rows.ShouldBeEmpty();
        none.Notes.ShouldNotBeEmpty();
        (await new GetLiveWaterfallHandler(bare.Store).HandleAsync(new LiveWaterfallQuery(99), Ct)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task A_second_run_writes_nothing_and_only_the_entry_outcomes_get_an_execution()
    {
        var rig = new Rig();
        await rig.SettledAsync();

        var first = await rig.Compute().HandleAsync(new ComputeLiveExecutionsCommand(LiveCaptureFixture.Day, null), Ct);
        var second = await rig.Compute().HandleAsync(new ComputeLiveExecutionsCommand(LiveCaptureFixture.Day, null), Ct);

        first.Value.ExecutionsWritten.ShouldBe(1);
        second.Value.ExecutionsWritten.ShouldBe(0);
        second.Value.OrderClassesWritten.ShouldBe(0);
        rig.Store.Executions.Count.ShouldBe(1); // T2 rejected, T3 missed, T4 not triggered have none
    }

    [Fact]
    public async Task Entry_orders_are_classified_full_and_rejected_and_the_stop_is_not_an_entry_order()
    {
        var rig = new Rig();
        await rig.SettledAsync();

        await rig.Compute().HandleAsync(new ComputeLiveExecutionsCommand(LiveCaptureFixture.Day, null), Ct);

        rig.Store.Classes.Select(c => (c.BrokerOrderId, c.Class)).OrderBy(c => c.BrokerOrderId).ToList().ShouldBe([("o1", OrderFillClass.Full), ("o3", OrderFillClass.Rejected)]);
        rig.Store.Classes.Single(c => c.BrokerOrderId == "o1").Bucket.ShouldBe("L1");
        rig.Store.Classes.Single(c => c.BrokerOrderId == "o3").Bucket.ShouldBe("unknown"); // no turnover known for TCS
    }

    [Fact]
    public async Task Without_order_evidence_there_is_no_arrival_no_delay_and_no_order_class_but_the_total_stands()
    {
        var rig = new Rig(orderBook: false);
        await rig.SettledAsync();

        await rig.Compute().HandleAsync(new ComputeLiveExecutionsCommand(LiveCaptureFixture.Day, null), Ct);

        var e = rig.Store.Executions.Single().Row;
        e.ArrivalPrice.ShouldBeNull();
        e.SlipDelay.ShouldBeNull();
        e.SlipExec.ShouldBeNull();
        e.SlipTotal.ShouldNotBeNull().ShouldBe(0.475, 1e-9);
        e.EntryDelaySeconds.ShouldBeNull();
        rig.Store.Classes.ShouldBeEmpty();
        var view = LiveExecutionMapping.Execution(rig.Store.Executions.Single());
        view.DelayRupees.State.ShouldBe("unavailable");
        view.DelayRupees.Number.ShouldBeNull();
        view.TotalRupees.Number.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_ticket_with_no_numeric_level_is_measured_against_the_plan_grade_and_with_neither_every_figure_is_unavailable()
    {
        var rig = new Rig();
        await rig.SettledAsync();
        var input = (LiveExecutionInput t1) => t1;
        var fills = rig.Fixture.Store.Fills.Where(f => f.Symbol == "RELIANCE" && f.Side == Side.Buy).ToList();
        var graded = new TicketGrade(GradeStatus.Graded, 0.5, null, null, "t", 1249.0, 1260, "gross, no charges", "x", 1239);
        var baseInput = new LiveExecutionInput(1, rig.Fixture.Account.Id, 1, LiveCaptureFixture.Day, "RELIANCE", Side.Buy, fills, [], null, null, graded, null, null, 0.05m, 640m, new Dictionary<Guid, decimal>());

        var viaGrade = LiveExecutionBuilder.Build(input(baseInput), BucketEdges.Default);
        viaGrade.ReferenceKind.ShouldBe(ReferenceKind.PlanGrade);
        viaGrade.ReferencePrice.ShouldBe(1249m);
        viaGrade.RiskPerShare.ShouldBe(10m);
        viaGrade.SlipTotal.ShouldNotBeNull().ShouldBe(1.475, 1e-9);

        var none = LiveExecutionBuilder.Build(baseInput with { Grade = null }, BucketEdges.Default);
        none.ReferenceKind.ShouldBe(ReferenceKind.Unavailable);
        none.ReferencePrice.ShouldBeNull();
        none.SlipTotal.ShouldBeNull();
        none.SlipTotalBps.ShouldBeNull();
        LiveExecutionMapping.Execution(new StoredLiveExecution(1, none, DateTimeOffset.UnixEpoch)).TotalRupees.State.ShouldBe("unavailable");
    }

    [Fact]
    public async Task A_queue_miss_is_a_limit_traded_through_and_counts_against_capacity()
    {
        var rig = new Rig();
        await rig.SettledAsync();
        // T3's buy limit at 1,251 that expired unfilled while the lows of its life reached 1,250.90.
        await rig.Fixture.Store.InsertOrdersAsync([LiveFixtureOrder(rig.Fixture.Account.Id, "o5", "INFY", "expired", 0)], Ct);
        rig.Market.Extremes = (1250.90m, 1255m);

        await rig.Compute().HandleAsync(new ComputeLiveExecutionsCommand(LiveCaptureFixture.Day, null), Ct);

        rig.Store.Classes.Single(c => c.BrokerOrderId == "o5").Class.ShouldBe(OrderFillClass.QueueMiss);
    }

    private static LiveOrder LiveFixtureOrder(Guid account, string id, string symbol, string status, long filled) =>
        new(account, id, symbol, Side.Buy, "CNC", "LIMIT", "DAY", 30, 1251m, null, status, filled, null, new DateTimeOffset(LiveCaptureFixture.Day.ToDateTime(new TimeOnly(9, 45)), TimeSpan.FromMinutes(330)), null, null, null, null, null, LiveCaptureFixture.Day);

    [Fact]
    public async Task Estimates_are_one_snapshot_per_week_and_a_second_run_writes_nothing()
    {
        var rig = new Rig();
        await rig.SettledAsync();
        await rig.Compute().HandleAsync(new ComputeLiveExecutionsCommand(LiveCaptureFixture.Day, null), Ct);
        var monday = new DateOnly(2026, 11, 2);

        var first = await rig.Estimates().HandleAsync(new ComputeLiveEstimatesCommand(monday), Ct);
        var second = await rig.Estimates().HandleAsync(new ComputeLiveEstimatesCommand(monday), Ct);

        first.IsSuccess.ShouldBeTrue();
        first.Value.SlippageWritten.ShouldBe(1);
        first.Value.CapacityWritten.ShouldBeGreaterThan(0);
        second.Value.SlippageWritten.ShouldBe(0);
        second.Value.CapacityWritten.ShouldBe(0);
        var row = rig.Store.Slippage.Single();
        row.State.ShouldBe("insufficient"); // one observation, at least 10 needed
        row.MeanBps.ShouldBeNull();
        row.N.ShouldBe(1);
        (await rig.Estimates().HandleAsync(new ComputeLiveEstimatesCommand(new DateOnly(2026, 11, 3)), Ct)).IsFailure.ShouldBeTrue(); // not a Monday
    }

    [Fact]
    public async Task The_proposal_exists_only_at_enough_observations_with_a_positive_lower_bound()
    {
        var rig = new Rig();
        var week = new DateOnly(2026, 11, 2);
        NewLiveExecution Exec(long id, double bps) => LiveExecutionBuilder.Build(
            new LiveExecutionInput(id, Guid.Empty, id, LiveCaptureFixture.Day, "RELIANCE", Side.Buy, [new LiveFill(Guid.NewGuid(), Guid.Empty, "t" + id, null, "RELIANCE", Side.Buy, "CNC", 10, 1000m + (decimal)(bps / 10), LiveCaptureFixture.Day, null)], [], 1000m, null, null, null, null, null, 640m, new Dictionary<Guid, decimal>()),
            BucketEdges.Default);
        await rig.Store.InsertExecutionsAsync([.. Enumerable.Range(1, 24).Select(i => Exec(i, 3 + (i % 3)))], Ct); // 3 to 5 bp, all worse than the reference

        await rig.Estimates().HandleAsync(new ComputeLiveEstimatesCommand(week), Ct);

        var row = rig.Store.Slippage.Single();
        row.State.ShouldBe("value");
        row.N.ShouldBe(24);
        row.MeanLo.ShouldNotBeNull().ShouldBeGreaterThan(0);
        row.Proposal.ShouldNotBeNull().ShouldContain("slippage.ticks");

        var small = new Rig();
        await small.Store.InsertExecutionsAsync([.. Enumerable.Range(1, 12).Select(i => Exec(i, 3 + (i % 3)))], Ct);
        await small.Estimates().HandleAsync(new ComputeLiveEstimatesCommand(week), Ct);
        small.Store.Slippage.Single().Proposal.ShouldBeNull(); // 12 observations: a mean, no proposal
        small.Store.Slippage.Single().State.ShouldBe("value");
    }

    [Fact]
    public async Task Two_reference_kinds_in_one_bucket_are_two_rows_never_averaged()
    {
        var rig = new Rig();
        var week = new DateOnly(2026, 11, 2);
        NewLiveExecution Exec(long id, decimal? ticket, TicketGrade? grade) => LiveExecutionBuilder.Build(
            new LiveExecutionInput(id, Guid.Empty, id, LiveCaptureFixture.Day, "RELIANCE", Side.Buy, [new LiveFill(Guid.NewGuid(), Guid.Empty, "t" + id, null, "RELIANCE", Side.Buy, "CNC", 10, 1001m, LiveCaptureFixture.Day, null)], [], ticket, null, grade, null, null, null, 640m, new Dictionary<Guid, decimal>()),
            BucketEdges.Default);
        var graded = new TicketGrade(GradeStatus.Graded, 0, null, null, "t", 995.0, 1000, "gross, no charges", "x", 990);
        await rig.Store.InsertExecutionsAsync([.. Enumerable.Range(1, 10).Select(i => Exec(i, 1000m, null)), .. Enumerable.Range(11, 10).Select(i => Exec(i, null, graded))], Ct);

        await rig.Estimates().HandleAsync(new ComputeLiveEstimatesCommand(week), Ct);

        rig.Store.Slippage.Select(s => s.ReferenceKind).Order().ToList().ShouldBe([ReferenceKind.PlanGrade, ReferenceKind.TicketLevel]);
        rig.Store.Slippage.Single(s => s.ReferenceKind == ReferenceKind.TicketLevel).MeanBps.ShouldNotBeNull().ShouldBe(10.0, 1e-9);
        rig.Store.Slippage.Single(s => s.ReferenceKind == ReferenceKind.PlanGrade).MeanBps.ShouldNotBeNull().ShouldBe(60.3015, 1e-3);
    }

    [Fact]
    public async Task The_read_views_state_empty_before_anything_is_stored_and_carry_the_evidence()
    {
        var rig = new Rig();
        var slippage = (await new GetLiveSlippageHandler(rig.Store, rig.Settings).HandleAsync(new LiveSlippageQuery(null), Ct)).Value;
        slippage.State.ShouldBe("empty");
        slippage.Rows.ShouldBeEmpty();
        slippage.Evidence.BucketEdgesCr.ShouldBe([10m, 50m, 150m, 500m]);
        slippage.Evidence.ReferenceKinds.ShouldContain(ReferenceKind.TicketLevel);
        slippage.Evidence.Limits.ShouldNotBeEmpty();
        (await new GetLiveCapacityHandler(rig.Store, rig.Settings).HandleAsync(new LiveCapacityQuery(null), Ct)).Value.State.ShouldBe("empty");
        (await new GetLiveSlippageHandler(rig.Store, rig.Settings).HandleAsync(new LiveSlippageQuery(new DateOnly(2026, 11, 3)), Ct)).IsFailure.ShouldBeTrue();

        await rig.SettledAsync();
        await rig.Compute().HandleAsync(new ComputeLiveExecutionsCommand(LiveCaptureFixture.Day, null), Ct);
        await rig.Estimates().HandleAsync(new ComputeLiveEstimatesCommand(new DateOnly(2026, 11, 2)), Ct);
        var after = (await new GetLiveSlippageHandler(rig.Store, rig.Settings).HandleAsync(new LiveSlippageQuery(null), Ct)).Value;
        after.State.ShouldBe("value");
        after.Rows.Single().Mean.State.ShouldBe("insufficient");
        after.Rows.Single().Mean.Number.ShouldBeNull();
        after.Rows.Single().N.ShouldBe(1);
        var capacity = (await new GetLiveCapacityHandler(rig.Store, rig.Settings).HandleAsync(new LiveCapacityQuery(null), Ct)).Value;
        capacity.Rows.First(r => r.Scope == "account").Score.Number.ShouldBeNull(); // 2 counted orders, 10 needed
        var page = (await new GetLiveExecutionsHandler(rig.Store, rig.Settings, rig.Fixture.Time).HandleAsync(new LiveExecutionsQuery(null, null, null, 1, 50), Ct)).Value;
        page.Total.ShouldBe(1);
        page.Items.Single().TotalBps.Number.ShouldNotBeNull().ShouldBe(3.8, 1e-9);
        (await new GetLiveExecutionsHandler(rig.Store, rig.Settings, rig.Fixture.Time).HandleAsync(new LiveExecutionsQuery(null, null, null, 1, 500), Ct)).IsFailure.ShouldBeTrue();
        (await new GetLiveExecutionsHandler(rig.Store, rig.Settings, rig.Fixture.Time).HandleAsync(new LiveExecutionsQuery(null, new DateOnly(2026, 1, 1), new DateOnly(2026, 11, 3), 1, 50), Ct)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_estimates_service_is_registered_paused_on_saturday_at_ten_and_applies_nothing()
    {
        var def = LiveEstimatesService.Definition;
        def.Id.ShouldBe("live-estimates");
        def.Defaults.Enabled.ShouldBeFalse();
        def.Defaults.Schedule.Weekday.ShouldBe(DayOfWeek.Saturday);
        def.Defaults.Schedule.Time.ShouldBe(new TimeOnly(10, 0));
        new LiveEstimatesService(null!, new FakeEstimateSettings()).FirstStartSettings()!.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void An_illiquid_symbol_clamps_the_participation_to_the_column()
    {
        var fill = new LiveFill(Guid.NewGuid(), Guid.Empty, "t1", null, "X", Side.Buy, "CNC", 1_000_000, 1000m, LiveCaptureFixture.Day, null);
        var input = new LiveExecutionInput(1, Guid.Empty, 1, LiveCaptureFixture.Day, "X", Side.Buy, [fill], [], 1000m, null, null, null, null, null, 0.01m, new Dictionary<Guid, decimal>());

        LiveExecutionBuilder.Build(input, BucketEdges.Default).ParticipationPct.ShouldBe(9999.999999m);
    }
}
