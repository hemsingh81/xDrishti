#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Replay;
using XDrishti.Application.Research;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research;
using XDrishti.Domain.Selection;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Replay;

/// <summary>SR-01 part A review: the fill path knows its instrument, the clamp holds on the fill and exit paths, the driver refuses what would give a flat curve, the two off-by-default switches, learning from the newest snapshot, and archived baskets.</summary>
public sealed class ReplayReviewTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly D1 = new(2026, 10, 5);
    private static readonly DateOnly D2 = new(2026, 10, 6);

    private static ReplayWorld Rising(params string[] symbols)
    {
        var world = new ReplayWorld().WithDefaults(symbols);
        for (var i = 0; i < symbols.Length; i++)
        {
            world.Rising(symbols[i], 0.7 + (1.3 * i));
        }

        return world;
    }

    [Fact]
    public async Task A_past_member_that_has_left_every_basket_is_still_filled_and_holds_its_slot()
    {
        var world = Rising("AAA", "CCC");
        world.Members.RemoveAll(m => m.Symbol == "CCC");
        world.Members.Add((ReplayWorld.Ids["CCC"], "CCC", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero))); // a member until the 20th: in no basket today
        var run = await ReplayRig.Create(world).Driver.RunAsync(ReplayRig.Params(2), Ct);

        var first = run.Value.Sessions[0];

        first.Taken.Select(t => t.Symbol).ShouldContain("CCC");
        first.Fills.Single(f => f.Symbol == "CCC").Status.ShouldBe(ReplayFill.Filled);
        run.Value.Sessions[1].OpenPositions.ShouldBe(2);
    }

    [Fact]
    public async Task A_runner_given_a_last_allowed_date_past_the_clamp_refuses_on_the_session_the_fill_and_the_exit()
    {
        var world = Rising("AAA");
        world.HoldOutStart = new DateOnly(2026, 10, 8);
        world.EmbargoDays = 2; // the last readable session is the 5th
        var rig = ReplayRig.Create(world);
        var context = new ReplayRunContext(ReplayRig.Params(5), await rig.Facts.SettingsAsync(Ct), CostChoice.Gross, null, await rig.Facts.CalendarAsync(Ct), new DateOnly(2026, 10, 30)); // a resume that builds the context itself

        await Should.ThrowAsync<ReplayHoldOutException>(async () => await rig.Runner.RunAsync(context, new DateOnly(2026, 10, 6), new ReplayBook(1_000_000m), Ct));
        // the 5th may be read, but the fill of its plan is on the 6th: past the clamp
        await Should.ThrowAsync<ReplayHoldOutException>(async () => await rig.Runner.RunAsync(context, D1, new ReplayBook(1_000_000m), Ct));
    }

    [Fact]
    public async Task With_no_hold_out_block_the_driver_refuses_with_the_holdout_code_not_an_exception()
    {
        var world = Rising("AAA");
        world.NoHoldOut = true;

        var result = await ReplayRig.Create(world).Driver.RunAsync(ReplayRig.Params(2), Ct);

        result.Error.Code.ShouldBe(ReplayDriver.HoldOutCode);
    }

    [Fact]
    public async Task A_start_with_no_frozen_cell_statistics_is_refused_so_a_run_never_draws_a_flat_curve()
    {
        var world = Rising("AAA");
        world.Snapshots.Clear();

        var result = await ReplayRig.Create(world).Driver.RunAsync(ReplayRig.Params(2), Ct);

        result.Error.Code.ShouldBe(ReplayDriver.InvalidCode);
        result.Error.Message.ShouldContain("no cell statistics");
    }

    [Fact]
    public async Task Filters_without_history_hold_candidates_back_by_default_and_the_switch_lets_them_through_with_its_label()
    {
        var strict = Rising("AAA");
        strict.Rules = new SelectionRules(OptionalFilters: [FilterCodes.Cost]); // event, F&O ban and surveillance are required (cost is the owner's own loosening, not the switch's)
        var off = await ReplayRig.Create(strict).Driver.RunAsync(ReplayRig.Params(2), Ct);
        var strictOn = Rising("AAA");
        strictOn.Rules = new SelectionRules(OptionalFilters: [FilterCodes.Cost]);
        var on = await ReplayRig.Create(strictOn).Driver.RunAsync(ReplayRig.Params(2) with { FiltersWithoutHistory = true }, Ct);

        off.Value.Sessions[0].Taken.ShouldBeEmpty();
        off.Value.Sessions[0].Blocked.ShouldAllBe(b => b.Reason == BlockReason.Gate);
        off.Value.Labels.ShouldNotContain(HindsightLabels.FiltersWithoutHistory);
        on.Value.Sessions[0].Taken.Select(t => t.Symbol).ShouldBe(["AAA"]);
        on.Value.Labels.ShouldContain(HindsightLabels.FiltersWithoutHistory);
    }

    private static XDrishti.Application.Planning.PlanListFilter Lists(bool enabled, Func<DateOnly, bool> covered)
    {
        var settings = NSubstitute.Substitute.For<XDrishti.Application.Abstractions.Market.Lists.IListFilterSettings>();
        settings.Enabled.Returns(enabled);
        var port = NSubstitute.Substitute.For<XDrishti.Application.Abstractions.Market.Lists.IListFlagsAsOf>();
        port.ForAsync(default!, default, default, default).ReturnsForAnyArgs(call => ValueTask.FromResult(new XDrishti.Application.Abstractions.Market.Lists.ListFlags(covered(call.ArgAt<DateOnly>(1)), covered(call.ArgAt<DateOnly>(2)), new HashSet<Guid>(), new HashSet<Guid>())));
        return new XDrishti.Application.Planning.PlanListFilter(settings, port, Microsoft.Extensions.Logging.Abstractions.NullLogger<XDrishti.Application.Planning.PlanListFilter>.Instance);
    }

    [Fact]
    public async Task The_filters_without_history_label_clears_only_when_every_session_has_a_stored_list_row_and_lists_the_uncovered_dates()
    {
        var strict = Rising("AAA");
        strict.Rules = new SelectionRules(OptionalFilters: [FilterCodes.Cost]);
        var all = await ReplayRig.Create(strict, lists: Lists(true, _ => true)).Driver.RunAsync(ReplayRig.Params(2) with { FiltersWithoutHistory = true }, Ct);
        var partialWorld = Rising("AAA");
        partialWorld.Rules = new SelectionRules(OptionalFilters: [FilterCodes.Cost]);
        var partial = await ReplayRig.Create(partialWorld, lists: Lists(true, d => d <= ReplayWorld.RunSessions[1])).Driver.RunAsync(ReplayRig.Params(2) with { FiltersWithoutHistory = true }, Ct);
        var offWorld = Rising("AAA");
        offWorld.Rules = new SelectionRules(OptionalFilters: [FilterCodes.Cost]);
        var off = await ReplayRig.Create(offWorld, lists: Lists(false, _ => true)).Driver.RunAsync(ReplayRig.Params(2) with { FiltersWithoutHistory = true }, Ct);

        all.Value.Labels.ShouldNotContain(HindsightLabels.FiltersWithoutHistory);
        all.Value.Labels.ShouldContain(HindsightLabels.EventFilterWithoutHistory, "the event filter has no history either way");
        off.Value.Sessions.SelectMany(x => x.Notes).ShouldNotContain(n => n.StartsWith("filters_without_history", StringComparison.Ordinal), "switch off: the record is what it was");
        off.Value.Labels.ShouldNotContain(HindsightLabels.EventFilterWithoutHistory);
        all.Value.Sessions[0].Taken.Select(t => t.Symbol).ShouldBe(["AAA"], "a covered day without a ban lets the share through even though the lists are required");
        partial.Value.Labels.ShouldContain(HindsightLabels.FiltersWithoutHistory);
        partial.Value.Sessions[0].Labels.ShouldNotContain(HindsightLabels.FiltersWithoutHistory);
        partial.Value.Sessions[1].Labels.ShouldContain(HindsightLabels.FiltersWithoutHistory);
        partial.Value.Sessions[1].Notes.ShouldContain(n => n.Contains(ReplayWorld.RunSessions[1].ToString("yyyy-MM-dd"), StringComparison.Ordinal));
        off.Value.Labels.ShouldContain(HindsightLabels.FiltersWithoutHistory, "switch off: no list is read and nothing is cleared");
    }

    [Fact]
    public async Task A_run_before_the_known_since_date_carries_the_survivorship_label_on_its_sessions_and_the_run()
    {
        var world = Rising("AAA");
        world.KnownSince = ReplayWorld.RunSessions[1];

        var run = await ReplayRig.Create(world).Driver.RunAsync(ReplayRig.Params(2), Ct);

        run.Value.Sessions[0].Labels.ShouldContain(HindsightLabels.Survivorship);
        run.Value.Labels.ShouldContain(HindsightLabels.Survivorship);
    }

    [Fact]
    public void The_read_side_drops_the_label_only_when_stored_sessions_do_not_carry_it()
    {
        var p = ReplayRig.Params(2) with { FiltersWithoutHistory = true };

        XDrishti.Application.Replay.Reads.ReplayReadSupport.Labels(p).ShouldContain(l => l.Code == HindsightLabels.FiltersWithoutHistory);
        XDrishti.Application.Replay.Reads.ReplayReadSupport.Labels(p, [HindsightLabels.SettingsNow]).ShouldNotContain(l => l.Code == HindsightLabels.FiltersWithoutHistory);
        XDrishti.Application.Replay.Reads.ReplayReadSupport.Labels(p, [HindsightLabels.SettingsNow, HindsightLabels.FiltersWithoutHistory]).ShouldContain(l => l.Code == HindsightLabels.FiltersWithoutHistory);
    }

    [Fact]
    public async Task Research_cells_are_not_candidates_by_default_and_the_switch_allows_them_with_its_label()
    {
        var world = Rising("AAA");
        world.Changelog[ReplayWorld.Cell.Key].Clear(); // the changelog is empty: the cell has no recorded state
        var off = await ReplayRig.Create(world).Driver.RunAsync(ReplayRig.Params(2), Ct);
        var on = await ReplayRig.Create(world).Driver.RunAsync(ReplayRig.Params(2) with { IncludeResearchCells = true }, Ct);

        off.Value.Sessions[0].Taken.ShouldBeEmpty();
        off.Value.Sessions[0].Blocked.ShouldAllBe(b => b.Reason == BlockReason.Gate);
        on.Value.Sessions[0].Taken.Select(t => t.Symbol).ShouldBe(["AAA"]);
        on.Value.Labels.ShouldContain(HindsightLabels.ResearchCells);
    }

    [Fact]
    public async Task Learning_reads_the_sources_of_the_newest_snapshot_and_the_as_of_filter_makes_the_cut()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        world.Snapshots.Add((new DateOnly(2026, 10, 30), ReplayWorld.Stats(new DateOnly(2026, 10, 30), 99) with { Sources = "newrun:swing:FIXED_RR{rr=2}" })); // made after the start, from a run the frozen snapshot does not name
        var trades = new[] { new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 14) }
            .Select(d => new RunTrade(FabricatedRun.Trade(ReplayWorld.Ids["AAA"], "AAA", Side.Buy, d, strategy: "S1") with { TriggerTimeframe = Timeframe.D1 }, [])).ToList();
        world.Runs.FindAsync("newrun", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<XDrishti.Application.Abstractions.Research.StoredRunHeader?>(ReplayWorld.Blank<XDrishti.Application.Abstractions.Research.StoredRunHeader>()));
        world.Runs.StreamTradesAsync("newrun", false, Arg.Any<CancellationToken>()).Returns(_ => ToAsync(trades));
        var p = ReplayRig.Params(5, mode: ReplayMode.Learning);

        var frozenWeek = await world.Facts().CellsAsync(p, new DateOnly(2026, 10, 7), Ct);
        var laterWeek = await world.Facts().CellsAsync(p, new DateOnly(2026, 10, 13), Ct); // Sunday the 11th: two trades have exited; the one of the 14th has not

        frozenWeek.Cells.Single().Stored.Row.N.ShouldBe(40); // the frozen snapshot (before the start)
        laterWeek.Cells.Single().Stored.Row.N.ShouldBe(2);
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
    public async Task A_basket_archived_on_or_before_the_cut_contributes_no_member()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        world.ArchivedAt = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(5.5));
        var facts = world.Facts();
        var p = ReplayRig.Params(5);

        (await facts.UniverseAsync(p, new DateOnly(2026, 10, 7), Ct)).Members.ShouldNotBeEmpty();
        (await facts.UniverseAsync(p, new DateOnly(2026, 10, 8), Ct)).Members.ShouldBeEmpty();
        (await facts.UniverseAsync(p, new DateOnly(2026, 10, 9), Ct)).Members.ShouldBeEmpty();
    }
}
