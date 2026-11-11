#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Replay;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Replay;

/// <summary>SR-01 AC 4: for every as-of fact a fixture holds values before and after the session, and the replay of the session reads only what was known at its close.</summary>
public sealed class ReplayFactsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D = ReplayWorld.Start;

    private static ReplayParams Params(ReplayMode mode = ReplayMode.Frozen, ReplayVersionRule rule = ReplayVersionRule.Start) =>
        new(D, D.AddDays(25), new ReplayScope([ReplayWorld.Basket], []), 1_000_000m, ReplayRiskProfile.Standard, null, rule, mode);

    [Fact]
    public async Task Members_are_those_of_the_close_of_the_session_not_those_added_or_removed_later()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        world.Members.Add((ReplayWorld.Ids["BBB"], "BBB", new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), null)); // joins on the 20th
        world.Members.Add((ReplayWorld.Ids["CCC"], "CCC", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero))); // left before the start
        world.Members.Add((ReplayWorld.Ids["DDD"], "DDD", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero))); // leaves on the 15th
        var facts = world.Facts();

        var early = await facts.UniverseAsync(Params(), new DateOnly(2026, 10, 9), Ct);
        var late = await facts.UniverseAsync(Params(), new DateOnly(2026, 10, 21), Ct);

        early.Members.Select(m => m.Symbol).ShouldBe(["AAA", "DDD"]);
        late.Members.Select(m => m.Symbol).ShouldBe(["AAA", "BBB"]);
    }

    [Fact]
    public async Task A_date_before_the_known_since_date_carries_the_survivorship_label_and_a_later_one_does_not()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        world.KnownSince = new DateOnly(2026, 10, 15);
        var facts = world.Facts();

        var before = await facts.UniverseAsync(Params(), new DateOnly(2026, 10, 9), Ct);
        var after = await facts.UniverseAsync(Params(), new DateOnly(2026, 10, 16), Ct);

        before.Survivorship.ShouldBeTrue();
        before.Members.ShouldNotBeEmpty(); // the earliest known membership, never an empty list
        after.Survivorship.ShouldBeFalse();
    }

    [Fact]
    public async Task Bars_stop_at_the_close_of_the_session_and_a_read_past_the_hold_out_clamp_fails_loudly()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        world.HoldOutStart = new DateOnly(2026, 11, 20);
        world.EmbargoDays = 2;
        var facts = world.Facts();
        var session = new DateOnly(2026, 10, 14);

        var bars = await facts.MinuteBarsAsync(ReplayWorld.Ids["AAA"], session, Ct);

        SessionClock.DateOf(bars[^1].EpochSeconds).ShouldBe(session);
        bars.ShouldAllBe(b => SessionClock.DateOf(b.EpochSeconds) <= session);
        (await facts.LastReadableSessionAsync(Ct)).ShouldBe(new DateOnly(2026, 11, 17)); // the start less one day less the embargo
        await Should.ThrowAsync<ReplayHoldOutException>(async () => await facts.MinuteBarsAsync(ReplayWorld.Ids["AAA"], new DateOnly(2026, 11, 18), Ct));
        await Should.ThrowAsync<ReplayHoldOutException>(async () => await facts.MarketAsync(new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 19), [], [], Ct));
    }

    [Fact]
    public async Task Minute_bars_before_the_instruments_high_water_seam_never_reach_the_replay()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        var seam = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.FromHours(5.5));
        world.Seams.SeamOfAsync(ReplayWorld.Ids["AAA"], Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<DateTimeOffset?>(seam));

        var bars = await world.Facts().MinuteBarsAsync(ReplayWorld.Ids["AAA"], new DateOnly(2026, 10, 14), Ct);

        bars.ShouldNotBeEmpty();
        bars.ShouldAllBe(b => b.EpochSeconds >= seam.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task With_no_hold_out_block_the_replay_cannot_read_a_bar()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        var facts = world.Facts();
        var holdOut = Substitute.For<XDrishti.Application.Abstractions.Lab.IHoldOutStore>();
        holdOut.CurrentAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<XDrishti.Application.Abstractions.Lab.HoldOutBlock?>(null));
        var bars = new ClampedBars(holdOut, new XDrishti.Domain.Confidence.HoldOutOptions(), Substitute.For<XDrishti.Application.Abstractions.Market.Bars.IBarStore>(), Substitute.For<XDrishti.Application.Abstractions.Market.History.IBackfillSeams>(), Substitute.For<XDrishti.Application.Abstractions.Market.Bars.IBarQueries>());

        await Should.ThrowAsync<ReplayHoldOutException>(async () => await bars.MinutesAsync(ReplayWorld.Ids["AAA"], D, Ct));
        (await facts.LastReadableSessionAsync(Ct)).ShouldBeGreaterThan(D); // the world's block exists: the control
    }

    [Fact]
    public async Task Under_the_as_of_rule_a_session_uses_the_latest_version_written_by_its_close_and_none_before_the_first()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        world.Versions["S1"] = [ReplayWorld.Version("S1", 1, ReplayWorld.Hash1, new DateTimeOffset(2026, 10, 7, 18, 0, 0, TimeSpan.FromHours(5.5))), ReplayWorld.Version("S1", 2, ReplayWorld.Hash2, new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(5.5)))];
        var facts = world.Facts();
        var p = Params(rule: ReplayVersionRule.AsOf);

        var first = await facts.CellsAsync(p, new DateOnly(2026, 10, 6), Ct);
        var second = await facts.CellsAsync(p, new DateOnly(2026, 10, 8), Ct);
        var third = await facts.CellsAsync(p, new DateOnly(2026, 10, 13), Ct);

        first.Cells.Single().Version.ShouldBeNull(); // no version existed at the close of the 6th: a no_version block, not a guess
        second.Cells.Single().Version!.Hash.ShouldBe(ReplayWorld.Hash1);
        third.Cells.Single().Version!.Hash.ShouldBe(ReplayWorld.Hash2);
    }

    [Fact]
    public async Task Under_the_start_rule_every_session_uses_the_version_active_at_the_start()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        world.Versions["S1"] = [ReplayWorld.Version("S1", 1, ReplayWorld.Hash1, new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero)), ReplayWorld.Version("S1", 2, ReplayWorld.Hash2, new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero))];
        var facts = world.Facts();

        var early = await facts.CellsAsync(Params(), new DateOnly(2026, 10, 6), Ct);
        var late = await facts.CellsAsync(Params(), new DateOnly(2026, 10, 20), Ct);

        early.Cells.Single().Version!.Hash.ShouldBe(ReplayWorld.Hash2); // today's version, run backwards (labelled versions_start)
        late.Cells.Single().Version!.Hash.ShouldBe(ReplayWorld.Hash2);
    }

    [Fact]
    public async Task Frozen_statistics_are_the_snapshot_before_the_start_for_the_whole_run_never_a_later_one()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        world.Snapshots.Add((new DateOnly(2026, 10, 12), ReplayWorld.Stats(new DateOnly(2026, 10, 12), 99))); // written after the start
        var facts = world.Facts();

        var onDay = await facts.CellsAsync(Params(), new DateOnly(2026, 10, 6), Ct);
        var weeksLater = await facts.CellsAsync(Params(), new DateOnly(2026, 10, 27), Ct);

        onDay.Cells.Single().Stored.Row.N.ShouldBe(40);
        weeksLater.Cells.Single().Stored.Row.N.ShouldBe(40);
    }

    [Fact]
    public async Task Learning_statistics_are_recomputed_in_memory_on_the_weekly_cadence_from_trades_that_exited_by_then()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        var trades = new[] { new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 21) }
            .Select((d, i) => new RunTrade(FabricatedRun.Trade(ReplayWorld.Ids["AAA"], "AAA", Side.Buy, d, strategy: "S1", rFilled: i % 2 == 0 ? 1.5 : -1.0) with { TriggerTimeframe = Timeframe.D1 }, [])).ToList();
        world.Runs.FindAsync("run", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<XDrishti.Application.Abstractions.Research.StoredRunHeader?>(ReplayWorld.Blank<XDrishti.Application.Abstractions.Research.StoredRunHeader>()));
        world.Runs.StreamTradesAsync("run", false, Arg.Any<CancellationToken>()).Returns(_ => ToAsync(trades));
        var facts = world.Facts();
        var p = Params(ReplayMode.Learning);

        var firstWeek = await facts.CellsAsync(p, new DateOnly(2026, 10, 7), Ct);   // the week of the start: the frozen rows
        var secondWeek = await facts.CellsAsync(p, new DateOnly(2026, 10, 13), Ct); // Sunday the 11th: the three trades of the 6th
        var thirdWeek = await facts.CellsAsync(p, new DateOnly(2026, 10, 20), Ct);  // Sunday the 18th: five trades; the two of the 21st are not known yet
        var fourthWeek = await facts.CellsAsync(p, new DateOnly(2026, 10, 27), Ct); // Sunday the 25th: all seven

        firstWeek.Cells.Single().Stored.Row.N.ShouldBe(40);
        secondWeek.Cells.Single().Stored.Row.N.ShouldBe(3);
        thirdWeek.Cells.Single().Stored.Row.N.ShouldBe(5);
        fourthWeek.Cells.Single().Stored.Row.N.ShouldBe(7);
        secondWeek.Notes.ShouldContain(HindsightLabels.LifecycleAsRecorded);
    }

    private static async IAsyncEnumerable<RunTrade> ToAsync(IEnumerable<RunTrade> trades)
    {
        foreach (var t in trades)
        {
            yield return t;
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Frozen_state_is_the_changelog_at_the_start_and_learning_state_is_the_changelog_as_recorded_at_the_session()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        world.Changelog[ReplayWorld.Cell.Key].Add(ReplayWorld.Transition(2, "candidate", "paper", new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(5.5))));
        var facts = world.Facts();

        var frozenLater = await facts.CellsAsync(Params(), new DateOnly(2026, 10, 20), Ct);
        var learningBefore = await facts.CellsAsync(Params(ReplayMode.Learning), new DateOnly(2026, 10, 13), Ct);
        var learningAfter = await facts.CellsAsync(Params(ReplayMode.Learning), new DateOnly(2026, 10, 15), Ct);

        frozenLater.Cells.Single().State.ShouldBe(CellState.Candidate); // the move of the 14th is after the start
        learningBefore.Cells.Single().State.ShouldBe(CellState.Candidate);
        learningAfter.Cells.Single().State.ShouldBe(CellState.Paper);
    }

    [Fact]
    public async Task The_market_gate_reads_the_latest_context_day_before_the_plan_date_never_a_later_day()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        world.ContextVix[new DateOnly(2026, 10, 7)] = 18;
        world.ContextVix[new DateOnly(2026, 10, 9)] = 31; // after the session of the 8th
        var facts = world.Facts();

        var market = await facts.MarketAsync(new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 9), [], [], Ct);

        market.Gate!.AsOf.ShouldBe(new DateOnly(2026, 10, 7));
        market.Gate.Vix.ShouldBe(18);
    }

    [Fact]
    public async Task Correlations_use_day_bars_up_to_the_session_only()
    {
        var honest = new ReplayWorld().WithDefaults("AAA", "BBB");
        var doctored = new ReplayWorld().WithDefaults("AAA", "BBB");
        var session = new DateOnly(2026, 10, 14);
        foreach (var symbol in new[] { "AAA", "BBB" })
        {
            foreach (var day in ReplayWorld.Sessions.Where(d => d > session))
            {
                doctored.Close[symbol][day] = symbol == "AAA" ? 500 : 3; // a crash and a spike after the session
            }
        }

        var ranked = new[] { new ContextInstrument(ReplayWorld.Ids["AAA"], "AAA"), new ContextInstrument(ReplayWorld.Ids["BBB"], "BBB") };
        var a = await honest.Facts().MarketAsync(session, session.AddDays(1), ranked, ranked, Ct);
        var b = await doctored.Facts().MarketAsync(session, session.AddDays(1), ranked, ranked, Ct);

        a.Correlations!.Get("AAA", "BBB").ShouldNotBeNull();
        b.Correlations!.Get("AAA", "BBB").ShouldBe(a.Correlations.Get("AAA", "BBB"));
    }

    [Fact]
    public async Task The_settings_snapshot_is_taken_once_and_a_later_change_is_not_seen()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        var facts = world.Facts();

        var first = await facts.SettingsAsync(Ct);
        world.BasketSettings = world.BasketSettings with { MaxClusterRiskPct = 1.5 };
        var second = await facts.SettingsAsync(Ct);

        second.ShouldBeSameAs(first);
        second.Basket.Settings.MaxClusterRiskPct.ShouldBeNull();
    }

    [Fact]
    public void Week_refresh_is_the_sunday_before_the_week_of_the_day()
    {
        ReplayFacts.WeekRefresh(new DateOnly(2026, 10, 7)).ShouldBe(new DateOnly(2026, 10, 4));
        ReplayFacts.WeekRefresh(new DateOnly(2026, 10, 12)).ShouldBe(new DateOnly(2026, 10, 11));
        ReplayFacts.WeekRefresh(new DateOnly(2026, 10, 11)).ShouldBe(new DateOnly(2026, 10, 4)); // a Sunday belongs to the week before
    }
}
