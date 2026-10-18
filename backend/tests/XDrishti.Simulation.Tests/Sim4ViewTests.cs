using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 8 of P3-03e: every row of the intraday fixtures and of the swing rows of P3-03d (M, G, J, Q) renders under the new engine version exactly as it did under sim-4 in the sim-4 view (<see cref="Sim4Rows.View"/>: the engine
/// label, and the two counters P3-03e appends, when zero). The frozen files (<c>Golden/*.sim-4.*</c>) were made with the sim-4 code before the first change of the slice and are never regenerated; <see cref="GoldenFile.Check"/> does not
/// read them. This slice changes the entry of a candidate decided at the end of its session and nothing else.
/// </summary>
public sealed class Sim4ViewTests
{
    private const string FrozenHeader = "engine sim-4\n";

    private static string Frozen(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", file));

    [Fact]
    public void The_fixtures_F_and_the_rows_W_render_as_the_frozen_sim_4_walker_file()
    {
        (FrozenHeader + Sim4Rows.Walker()).ShouldBe(Frozen("walker.sim-4.frozen.txt"));
    }

    [Fact]
    public void The_hand_trades_H1_to_H25_render_as_the_frozen_sim_4_file_modelled_and_exact()
    {
        (FrozenHeader + Sim4Rows.HRows()).ShouldBe(Frozen("hrows.sim-4.frozen.txt"));
    }

    [Fact]
    public void The_500_seeded_intraday_days_render_in_ten_batches_whose_hashes_are_the_frozen_sim_4_ones()
    {
        (FrozenHeader + Sim4Rows.SeededBatches()).ShouldBe(Frozen("seeded-intraday.sim-4.sha256.txt"));
    }

    [Fact]
    public void The_swing_rows_M_fixture_R_and_the_flagged_boundary_Q1_render_as_the_frozen_sim_4_file()
    {
        (FrozenHeader + Sim4Rows.Swing()).ShouldBe(Frozen("swing.sim-4.frozen.txt"));
    }

    [Fact]
    public void The_gate_rows_G_and_the_window_rows_J_and_Q2_render_as_the_frozen_sim_4_file()
    {
        (FrozenHeader + Sim4Rows.GateAndWindowRows()).ShouldBe(Frozen("gate-window.sim-4.frozen.txt"));
    }

    [Fact]
    public void The_frozen_files_keep_their_first_line_the_sim_4_engine_header()
    {
        foreach (var file in new[] { "walker.sim-4.frozen.txt", "hrows.sim-4.frozen.txt", "seeded-intraday.sim-4.sha256.txt", "swing.sim-4.frozen.txt", "gate-window.sim-4.frozen.txt" })
        {
            Frozen(file).Split('\n')[0].ShouldBe("engine sim-4", file);
        }
    }

    [Fact]
    public void The_view_hides_the_new_counters_only_when_they_are_zero_so_a_changed_count_cannot_pass()
    {
        Sim4Rows.View("a=1 overnightEntries=0 orderPendingAtEnd=0 b=2").ShouldBe("a=1 b=2");
        Sim4Rows.View("a=1 overnightEntries=3 orderPendingAtEnd=0").ShouldBe("a=1 overnightEntries=3");
        Sim4Rows.View("a=1 orderPendingAtEnd=10").ShouldBe("a=1 orderPendingAtEnd=10");
    }
}
