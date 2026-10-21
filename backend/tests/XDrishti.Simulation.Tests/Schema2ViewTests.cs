using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 14 of P3-03d: an intraday trade is byte-identical under sim-3 and sim-4 in the schema-2 view. The three frozen files (<c>Golden/*.sim-2.*</c>) were made with the sim-2 code before the first change of the slice and are never
/// regenerated (<see cref="GoldenFile.Check"/> does not read them); <see cref="Schema2View"/> prints exactly the 30 fields and the 20 counters of schema 2 and writes <c>schema=2</c> itself. The two appended fields are 1 and 0 on every
/// one of these trades.
/// </summary>
public sealed class Schema2ViewTests
{
    private const string FrozenHeader = "engine sim-2\n";

    private static string Frozen(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", file));

    [Fact]
    public void The_fixtures_and_rows_F1_to_F17b_and_W1_to_W14_render_as_the_frozen_walker_file_in_the_schema_2_view()
    {
        var rendered = FrozenHeader + Schema2View.Walker();

        rendered.ShouldBe(Frozen("walker.sim-2.frozen.txt"));
    }

    [Fact]
    public void The_hand_trades_H1_to_H25_render_as_the_frozen_file_in_the_schema_2_view_modelled_and_with_no_slippage()
    {
        var rendered = FrozenHeader + Schema2View.HRows();

        rendered.ShouldBe(Frozen("hrows.sim-2.frozen.txt"));
    }

    [Fact]
    public void The_500_seeded_intraday_days_render_in_ten_batches_whose_hashes_are_the_frozen_ones()
    {
        var rendered = FrozenHeader + Schema2View.SeededBatches();

        rendered.ShouldBe(Frozen("seeded-intraday.sim-2.sha256.txt"));
    }

    [Fact]
    public void Every_intraday_trade_of_the_fixtures_the_hand_rows_and_the_seeded_days_holds_one_session_and_sees_all_of_them()
    {
        var trades = Cases.All.SelectMany(c => c.Items.Select(i => WalkAssert.Walk(c, i).Value))
            .Concat(HandCases.All.Select(h => h.Hand.Walk(FillRules.Default)))
            .Concat(WalkProperties.WalkAll(SeededDays.All, FillRules.Default).Select(w => w.Outcome))
            .Where(o => o.Trade is not null)
            .ToList();

        trades.Count.ShouldBeGreaterThan(1_000);
        trades.ShouldAllBe(o => o.Trade!.SessionsHeld == 1 && o.Trade.SessionsUnseen == 0 && o.Trade.SchemaVersion == 5);
        trades.ShouldAllBe(o => o.Trade!.ExitReason != ExitReason.MaxHold && !o.MaxHoldAtLastClose && !o.OvernightStopGap && !o.OvernightTargetGap);
    }

    [Fact]
    public void Intraday_trades_are_flat_at_square_off_every_exit_is_on_the_entry_date_over_the_500_seeded_days_and_the_three_square_off_times()
    {
        var walks = WalkProperties.WalkAll(SeededDays.All, FillRules.Default);
        var trades = walks.Where(w => w.Trade is not null).ToList();

        SeededDays.All.Select(d => d.SquareOffMinute).Distinct().Order().ShouldBe([(14 * 60) + 30, (15 * 60) + 15, (15 * 60) + 29]);
        trades.Count.ShouldBeGreaterThan(1_000);
        trades.ShouldAllBe(w => SessionClock.DateOf(w.Trade!.ExitEpochSeconds) == w.Trade.TradeDate && w.Trade.ExitReason != ExitReason.MaxHold);
    }

    [Fact]
    public void The_new_counters_are_zero_on_every_intraday_run_except_the_sessions_held_which_equal_the_trades()
    {
        foreach (var walk in WalkProperties.WalkAll(SeededDays.All, FillRules.Default))
        {
            var c = walk.Outcome.ToCounters();

            (c.PositionOpen, c.ExitsMaxHold, c.MaxHoldAtLastClose, c.OvernightStopGaps, c.OvernightTargetGaps, c.SessionsUnseen).ShouldBe((0L, 0L, 0L, 0L, 0L, 0L), "seed " + walk.Day.Seed);
            c.SessionsHeld.ShouldBe(c.Trades, "seed " + walk.Day.Seed);
        }
    }

    [Fact]
    public void The_frozen_files_keep_their_first_line_the_sim_2_engine_header()
    {
        foreach (var file in new[] { "walker.sim-2.frozen.txt", "hrows.sim-2.frozen.txt", "seeded-intraday.sim-2.sha256.txt" })
        {
            Frozen(file).Split('\n')[0].ShouldBe("engine sim-2", file);
        }
    }
}
