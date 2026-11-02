namespace XDrishti.Simulation.Tests;

/// <summary>
/// P15-09 criterion 7: the differential test of the engine bump to <c>sim-9</c> (longer hold of an event side, the <c>event_exit</c> overlay, the <c>atr_mult</c> stop, the bucket features). The golden files of <c>sim-8</c> were frozen before the slice (<c>Golden/*.sim-8.frozen.txt</c>, copies of
/// the checked-in files; never regenerated). Every golden of <c>sim-9</c> equals its frozen <c>sim-8</c> file with the engine label replaced and, in the run record, the key hash (it has the engine label in its pre-image) left out of the comparison: a strategy that is not an event side and does
/// not name <c>event_exit</c> has the candidates, the trades and the counters it had under <c>sim-8</c>. The goldens are regenerated on purpose with <c>XD_UPDATE_GOLDEN=1</c>; this test is what proves the diff is the label.
/// </summary>
public sealed class EventHoldDifferentialTests
{
    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", file));

    private static string Normalised(string text) =>
        string.Join('\n', text.Split('\n').Where(l => !l.StartsWith("run schema=", StringComparison.Ordinal)).Select(l => l.Replace("sim-8", "sim-9", StringComparison.Ordinal)));

    [Theory]
    [InlineData("costed")]
    [InlineData("run-record")]
    [InlineData("scanner")]
    [InlineData("simulator")]
    [InlineData("swing")]
    [InlineData("vwap-pullback")]
    [InlineData("walker")]
    public void A_golden_of_sim_9_equals_the_frozen_golden_of_sim_8_but_for_the_engine_label_and_the_schema_and_key_line(string name)
    {
        var frozen = Read(name + ".sim-8.frozen.txt");
        var current = Read(name + ".golden.txt");

        frozen.Split('\n')[0].ShouldBe("engine sim-8", "the frozen file keeps its sim-8 header");
        current.Split('\n')[0].ShouldBe("engine sim-9");
        Normalised(current).ShouldBe(Normalised(frozen));
    }

    [Fact]
    public void The_key_line_of_the_run_record_is_the_only_line_the_comparison_leaves_out_and_it_did_change_with_the_engine_label_alone()
    {
        var frozen = Read("run-record.sim-8.frozen.txt").Split('\n').Single(l => l.StartsWith("run schema=", StringComparison.Ordinal));
        var current = Read("run-record.golden.txt").Split('\n').Single(l => l.StartsWith("run schema=", StringComparison.Ordinal));

        frozen.ShouldStartWith("run schema=8 key=");
        current.ShouldStartWith("run schema=8 key=");
        frozen.ShouldEndWith("engine=sim-8");
        current.ShouldEndWith("engine=sim-9");
        current.ShouldNotBe(frozen);
    }
}
