namespace XDrishti.Simulation.Tests;

/// <summary>
/// P15-06 criterion 4: the differential test of the engine bump. The golden files of <c>sim-6</c> were frozen before the slice (<c>Golden/*.sim-6.frozen.txt</c>, copies of the checked-in files; never regenerated). Every golden of <c>sim-7</c> equals its frozen sim-6 file with the engine label
/// replaced and, in the run record, the schema number and the key hash (both are in the key's pre-image) left out of the comparison: a strategy that does not name <c>not_event_day</c> has the candidates, the trades and the counters it had under sim-6.
/// </summary>
public sealed class EventFilterDifferentialTests
{
    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", file));

    private static string Normalised(string text) =>
        string.Join('\n', text.Split('\n').Where(l => !l.StartsWith("run schema=", StringComparison.Ordinal)).Select(l => l.Replace("sim-6", "sim-7", StringComparison.Ordinal)));

    [Theory]
    [InlineData("costed")]
    [InlineData("run-record")]
    [InlineData("scanner")]
    [InlineData("simulator")]
    [InlineData("swing")]
    [InlineData("vwap-pullback")]
    [InlineData("walker")]
    public void A_golden_of_sim_7_equals_the_frozen_golden_of_sim_6_but_for_the_engine_label_and_the_schema_and_key_line(string name)
    {
        var frozen = Read(name + ".sim-6.frozen.txt");
        var current = Read(name + ".golden.txt");

        frozen.Split('\n')[0].ShouldBe("engine sim-6", "the frozen file keeps its sim-6 header");
        current.Split('\n')[0].ShouldBe("engine sim-7");
        Normalised(current).ShouldBe(Normalised(frozen));
    }

    [Fact]
    public void The_schema_and_key_line_of_the_run_record_is_the_only_line_the_comparison_leaves_out_and_it_did_change()
    {
        var frozen = Read("run-record.sim-6.frozen.txt").Split('\n').Single(l => l.StartsWith("run schema=", StringComparison.Ordinal));
        var current = Read("run-record.golden.txt").Split('\n').Single(l => l.StartsWith("run schema=", StringComparison.Ordinal));

        frozen.ShouldStartWith("run schema=6 key=");
        current.ShouldStartWith("run schema=7 key=");
        current.ShouldEndWith("engine=sim-7");
        current.ShouldNotBe(frozen);
    }
}
