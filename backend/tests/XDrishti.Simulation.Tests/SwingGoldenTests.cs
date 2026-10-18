using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// The golden text of the swing rows (P3-03d, criterion 13 and 16): the trades M1 to M7b as the walker renders them, fixture R through the simulator with its trace (the same candidate, the same trade, the same counters) and the replay
/// record of the flagged boundary Q1. A new file next to the five regenerated ones; see <see cref="GoldenFile"/> (first line the engine version, <c>XD_UPDATE_GOLDEN=1</c> rewrites it).
/// </summary>
public sealed class SwingGoldenTests
{
    [Fact]
    public void The_golden_file_equals_the_rendering_of_the_swing_rows_fixture_R_and_the_flagged_boundary()
    {
        GoldenFile.Check("swing", RenderAll());
    }

    [Fact]
    public void The_rendering_is_the_same_twice()
    {
        RenderAll().ShouldBe(RenderAll());
    }

    private static string RenderAll() => Sim4Rows.SwingText();
}
