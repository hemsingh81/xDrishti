using XDrishti.Application.Replay;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Replay;

/// <summary>SR-01 AC 6 and 7 and the session step: portfolio limits are real and name their reason, a swing position holds its slot until its exit, equity comes from the paper ledger, and a session with too little data takes nothing.</summary>
public sealed class ReplaySessionRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D1 = new(2026, 10, 5);
    private static readonly DateOnly D2 = new(2026, 10, 6);
    private static readonly DateOnly D3 = new(2026, 10, 7);
    private static readonly DateOnly D4 = new(2026, 10, 8);
    private static readonly DateOnly D5 = new(2026, 10, 9);
    private static readonly DateOnly D6 = new(2026, 10, 12);

    private static async Task<ReplayRunResult> Run(ReplayRig rig, ReplayParams p)
    {
        var result = await rig.Driver.RunAsync(p, Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    private static ReplaySessionRecord On(ReplayRunResult run, DateOnly day) => run.Sessions.Single(s => s.Session == day);

    private static FillConstraints Limits(int open = 5, int entries = 3, double totalRisk = 3.0) =>
        new(MaxEntriesPerDay: entries, MaxOpenPositions: open, MaxTotalOpenRiskPct: totalRisk, MaxPerSector: 10, MaxPairCorrelation: 1.0, MaxIntraday: 10, MaxSwing: 10, MaxNetLongRiskPct: 50, MaxNetShortRiskPct: 50, MaxPerTradeRiskPct: 2.0);

    private static ReplayWorld Rising(Func<ReplayWorld, ReplayWorld>? tune = null, params string[] symbols)
    {
        var world = new ReplayWorld().WithDefaults(symbols);
        for (var i = 0; i < symbols.Length; i++)
        {
            world.Rising(symbols[i], 0.7 + (1.3 * i));
        }

        return tune?.Invoke(world) ?? world;
    }

    [Fact]
    public async Task A_session_inside_a_released_hold_out_block_carries_its_seen_holdout_label_and_one_before_it_does_not()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        world.Series("AAA", d => 100.0);
        world.ReleasedBlock = (D2, D4);

        var run = await Run(ReplayRig.Create(world), ReplayRig.Params(6));

        On(run, D1).Labels.ShouldNotContain("seen_holdout:2026-10-06");
        foreach (var day in new[] { D2, D3, D4 })
        {
            On(run, day).Labels.ShouldContain("seen_holdout:2026-10-06");
        }

        On(run, D5).Labels.ShouldNotContain("seen_holdout:2026-10-06");
        run.Labels.ShouldContain("seen_holdout:2026-10-06");
    }

    [Fact]
    public async Task A_taken_ticket_is_filled_on_the_next_session_and_the_swing_position_holds_its_slot_until_its_exit()
    {
        var world = new ReplayWorld().WithDefaults("AAA", "BBB");
        world.Fill = Limits(open: 1, entries: 1);
        world.Series("AAA", d => d < D1 ? 99.9 : d <= D5 ? 100.0 : 105.0); // entry 100 on the 6th, the target (104) is reached on the 12th
        world.Series("BBB", d => d <= D2 ? 100.0 : 100.0 + ((d.DayNumber - D2.DayNumber) * 0.1)); // a candidate every session from the 7th
        var run = await Run(ReplayRig.Create(world), ReplayRig.Params(6));

        var first = On(run, D1);
        first.Taken.Select(t => t.Symbol).ShouldBe(["AAA"]);
        var fill = first.Fills.Single();
        (fill.Status, fill.Session, fill.EntryPrice).ShouldBe((ReplayFill.Filled, D2, 100m));
        fill.Qty.ShouldBeGreaterThan(0);
        foreach (var day in new[] { D2, D3, D4, D5 })
        {
            On(run, day).OpenPositions.ShouldBe(1); // the swing position is open at the close of each session until its exit
        }

        foreach (var day in new[] { D3, D4, D5 })
        {
            var blocked = On(run, day).Blocked.Single(b => b.Symbol == "BBB");
            blocked.Reason.ShouldBe(BlockReason.MaxPositions); // the one slot is held
            On(run, day).Taken.ShouldBeEmpty();
        }

        var exitDay = On(run, D6);
        exitDay.Exits.ShouldBe(["AAA"]);
        exitDay.OpenPositions.ShouldBe(0);
        exitDay.Equity.ShouldBe(1_000_000m + (5m * fill.Qty)); // (105 - 100) x quantity, gross
        exitDay.Taken.ShouldNotBeEmpty(); // the slot is free again
    }

    [Fact]
    public async Task The_position_limit_blocks_with_the_max_positions_reason()
    {
        var world = Rising(w => w, "AAA", "BBB", "CCC");
        world.Fill = Limits(open: 1);
        var run = await Run(ReplayRig.Create(world), ReplayRig.Params(1));

        var record = On(run, D1);

        record.Taken.Count.ShouldBe(1);
        record.Blocked.Count.ShouldBe(2);
        record.Blocked.ShouldAllBe(b => b.Reason == BlockReason.MaxPositions);
    }

    [Fact]
    public async Task The_total_open_risk_limit_blocks_with_the_total_risk_reason()
    {
        var world = Rising(w => w, "AAA", "BBB", "CCC");
        world.Fill = Limits(totalRisk: 0.6); // each ticket risks 0.5 % of equity: a second one would make 1.0 %
        var run = await Run(ReplayRig.Create(world), ReplayRig.Params(1));

        var record = On(run, D1);

        record.Taken.Count.ShouldBe(1);
        record.Blocked.ShouldAllBe(b => b.Reason == BlockReason.TotalRisk);
        record.Blocked.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_cluster_cap_blocks_a_second_ticket_of_one_cluster_with_the_cluster_cap_reason()
    {
        var world = new ReplayWorld().WithDefaults("AAA", "BBB", "CCC");
        world.Rising("AAA", 0.7);
        world.Rising("BBB", 0.7); // the same moves as AAA: one cluster
        world.Rising("CCC", 3.1); // its own moves
        world.Fill = Limits();
        world.Smart = new SmartRulesOptions(CorrelationEnabled: false); // the same-bet rule would set the twin aside before the fill: this test is about the cluster cap
        world.BasketSettings = new BasketSettings("tracked", 0.6, 0.7); // a cluster may hold 0.6 % of equity at risk; each ticket risks 0.5 %
        var run = await Run(ReplayRig.Create(world), ReplayRig.Params(1));

        var record = On(run, D1);

        record.Blocked.Single().Reason.ShouldBe(BlockReason.ClusterCap);
        record.Taken.Select(t => t.Symbol).ShouldContain("CCC");
        record.Taken.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_cell_with_no_version_at_the_session_is_blocked_with_no_version_until_a_version_exists()
    {
        var world = Rising(w => w, "AAA");
        world.Versions["S1"] = [ReplayWorld.Version("S1", 1, ReplayWorld.Hash1, new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.FromHours(5.5)))];
        var run = await Run(ReplayRig.Create(world), ReplayRig.Params(5, rule: ReplayVersionRule.AsOf));

        foreach (var day in new[] { D1, D2, D3 })
        {
            On(run, day).Candidates.ShouldBeEmpty();
            On(run, day).Blocked.Single().ShouldBe(new ReplayBlocked(string.Empty, ReplayWorld.Cell.Key, BlockReason.NoVersion, "no_version"));
        }

        On(run, D4).Candidates.ShouldNotBeEmpty(); // written at 10:00 on the 8th: known at that close
        On(run, D4).Blocked.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_session_with_too_much_of_the_universe_incomplete_takes_no_new_entry_but_open_positions_still_exit()
    {
        var world = new ReplayWorld().WithDefaults("AAA", "BBB", "CCC", "DDD", "EEE");
        world.Fill = Limits();
        world.Series("AAA", d => d < D1 ? 99.9 : d <= D2 ? 100.0 : 105.0); // the only ticket of the 5th; its target is reached on the 7th
        foreach (var flat in new[] { "BBB", "CCC", "DDD", "EEE" })
        {
            world.Series(flat, d => d <= D2 ? 100.0 : 100.0 + ((d.DayNumber - D2.DayNumber) * 0.1)); // candidates from the 7th
        }

        world.Close["BBB"].Remove(D3);
        world.Close["CCC"].Remove(D3); // 2 of 5 incomplete on the 7th: 0.4 is more than 0.2
        var run = await Run(ReplayRig.Create(world), ReplayRig.Params(3));

        var record = On(run, D3);

        record.Exits.ShouldBe(["AAA"]);
        record.Taken.ShouldBeEmpty();
        record.Fills.ShouldBeEmpty();
        record.Blocked.ShouldNotBeEmpty();
        record.Blocked.ShouldAllBe(b => b.Reason == BlockReason.DataMissing);
        record.Notes.ShouldContain(n => n.StartsWith("data_missing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exactly_the_configured_share_incomplete_is_not_data_missing()
    {
        var world = Rising(w => w, "AAA", "BBB", "CCC", "DDD", "EEE");
        world.Close["EEE"].Remove(D2); // 1 of 5 = 0.2: not more than 0.2
        var run = await Run(ReplayRig.Create(world), ReplayRig.Params(2));

        On(run, D2).Notes.ShouldNotContain(n => n.StartsWith("data_missing", StringComparison.Ordinal));
        On(run, D2).Blocked.ShouldNotContain(b => b.Reason == BlockReason.DataMissing);
    }

    [Fact]
    public async Task The_final_equity_is_the_capital_plus_the_sum_of_the_net_trade_results_from_the_paper_ledger()
    {
        var world = Rising(w => w, "AAA", "BBB", "CCC");
        world.Smart = new SmartRulesOptions(CorrelationEnabled: false); // two flat-then-moving series are alike; the same-bet rule is not what this test is about
        world.Series("AAA", d => d < D1 ? 99.9 : d <= D2 ? 100.0 : d <= D3 ? 105.0 : 105.0 + ((d.DayNumber - D3.DayNumber) * 0.01)); // a winner: in at 100 on the 6th, out at 105 on the 7th
        world.Series("BBB", d => d < D1 ? 99.9 : d <= D2 ? 100.0 : d <= D3 ? 97.0 : 97.0 + ((d.DayNumber - D3.DayNumber) * 0.01)); // a loser: out at the stop on the 7th
        var run = await Run(ReplayRig.Create(world), ReplayRig.Params(6));

        var closed = run.Book.Trades.Where(t => t.Exit is not null && t.ExitSession <= ReplayRig.Params(6).To).ToList();
        closed.ShouldNotBeEmpty();
        var sum = closed.Sum(t => t.Exit!.NetPnl ?? t.Exit.GrossPnl);

        run.Final.Equity.ShouldBe(1_000_000m + sum);
        run.Sessions[^1].Equity.ShouldBe(run.Final.Equity!.Value);
        PaperLedger.Compute(1_000_000m, run.Book.LedgerAsOf(ReplayRig.Params(6).To), ReplayRig.Params(6).To).Equity.ShouldBe(run.Final.Equity); // one implementation, read two ways
        closed.Any(t => t.Exit!.GrossPnl < 0).ShouldBeTrue("the loser is in the book");
        run.Final.MaxDrawdown.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public async Task Sizing_compounds_a_ticket_after_a_win_is_sized_from_the_grown_equity()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        world.Series("AAA", d => d < D1 ? 99.9 : d <= D2 ? 100.0 : 110.0); // in at 100 on the 6th, out at 110 on the 7th (+10 per share); a new candidate on the 7th at 110
        var run = await Run(ReplayRig.Create(world), ReplayRig.Params(5));

        // hand-worked: the stop is 2 % below the price, the risk 0.5 % of equity: floor(0.005 x equity / (0.02 x price))
        On(run, D1).Fills.Single().Qty.ShouldBe(2500); // floor(5,000 / 2.00)
        On(run, D3).Equity.ShouldBe(1_025_000m); // + 10 x 2,500
        On(run, D3).Fills.Single().Qty.ShouldBe(2329); // floor(5,125 / 2.20); from the starting capital it would be floor(5,000 / 2.20) = 2,272
    }

    [Fact]
    public async Task A_run_without_a_sheet_is_gross_and_labelled_before_costs_and_with_a_sheet_it_is_not()
    {
        var world = Rising(w => w, "AAA");
        var gross = await Run(ReplayRig.Create(world), ReplayRig.Params(2));
        var net = await Run(ReplayRig.Create(Rising(w => w, "AAA")), ReplayRig.Params(2, PaperFixture.SheetId));

        gross.Labels.ShouldContain(HindsightLabels.BeforeCosts);
        On(gross, D1).Labels.ShouldContain(HindsightLabels.BeforeCosts);
        net.Labels.ShouldNotContain(HindsightLabels.BeforeCosts);
        gross.Labels.ShouldContain(HindsightLabels.VersionsStart);
        gross.Labels.ShouldContain(HindsightLabels.SettingsNow);
        gross.Labels.ShouldContain(HindsightLabels.DataSeenBefore);
    }

    [Fact]
    public async Task A_range_that_reaches_the_hold_out_is_refused_with_the_last_allowed_date_and_nothing_is_read()
    {
        var world = Rising(w => w, "AAA");
        world.HoldOutStart = new DateOnly(2026, 10, 20);
        world.EmbargoDays = 2; // the last readable session is the 17th
        var rig = ReplayRig.Create(world);

        var result = await rig.Driver.RunAsync(ReplayRig.Params(6) with { To = new DateOnly(2026, 10, 19) }, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReplayDriver.HoldOutCode);
        result.Error.Message.ShouldContain("2026-10-17");
        rig.Simulation.MaxDateRead.ShouldBe(default);
    }

    [Fact]
    public async Task A_run_to_the_last_allowed_date_never_reads_past_it_and_takes_no_entry_after_the_end()
    {
        var world = Rising(w => w, "AAA");
        world.HoldOutStart = new DateOnly(2026, 10, 12);
        world.EmbargoDays = 2; // the last readable session is the 9th
        var rig = ReplayRig.Create(world);

        var run = await Run(rig, ReplayRig.Params(5));

        run.Sessions[^1].Session.ShouldBe(D5);
        run.Sessions[^1].Notes.ShouldContain(ReplaySessionRunner.EndOfRangeNote);
        run.Sessions[^1].Fills.ShouldBeEmpty();
        rig.Simulation.MaxDateRead.ShouldBeLessThanOrEqualTo(D5);
    }

    [Fact]
    public async Task Bad_parameters_are_refused_with_the_invalid_code()
    {
        var rig = ReplayRig.Create(Rising(w => w, "AAA"));

        (await rig.Driver.RunAsync(ReplayRig.Params(2) with { From = new DateOnly(2026, 10, 20) }, Ct)).Error.Code.ShouldBe(ReplayDriver.InvalidCode); // from after to
        (await rig.Driver.RunAsync(ReplayRig.Params(2) with { Capital = 0 }, Ct)).Error.Code.ShouldBe(ReplayDriver.InvalidCode);
        (await rig.Driver.RunAsync(ReplayRig.Params(2) with { Scope = new ReplayScope([], []) }, Ct)).Error.Code.ShouldBe(ReplayDriver.InvalidCode);
    }

    [Fact]
    public void The_book_at_a_session_knows_no_exit_after_it()
    {
        var item = new XDrishti.Application.Abstractions.Planning.NewPlanItem(1, XDrishti.Domain.Planning.TicketGroup.Primary, "AAA", "BUY", "S1", ReplayWorld.Hash1, "t", "s", "g", "v", [], new XDrishti.Application.Abstractions.Planning.PlanEvidence(ReplayWorld.Cell.Key, 1, "r", "b", null, null, [], null), []);
        var entry = new DateTimeOffset(2026, 10, 6, 9, 15, 0, TimeSpan.FromHours(5.5));
        var trade = new ReplayTrade(1, item, D2, 10, null, entry, 100m, 98m, true, "k")
        {
            Exit = new PaperExit(new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.FromHours(5.5)), 105m, "Target", 4, 50m, null, null, null, "gross", 2, 2, null, 2, "gross", 0, 2),
        };
        var book = new ReplayBook(1_000m);
        book.Add(trade);

        book.OpenAt(D3).Count.ShouldBe(1); // the exit of the 9th is not known on the 7th
        book.FiguresAsOf(D3).OpenCount.ShouldBe(1);
        book.FiguresAsOf(D3).Equity.ShouldBe(1_000m);
        book.OpenAt(D5).ShouldBeEmpty();
        book.FiguresAsOf(D5).Equity.ShouldBe(1_050m);
        book.LedgerAsOf(D1).ShouldBeEmpty(); // not yet entered on the 5th
    }
}
