namespace XDrishti.Simulation.Tests;

/// <summary>
/// P15-05 criterion 7: the differential test of the engine bump to <c>sim-8</c>. The golden files of <c>sim-7</c> were frozen before the slice (<c>Golden/*.sim-7.frozen.txt</c>, copies of the checked-in files; never regenerated). Every golden of <c>sim-8</c> equals its frozen
/// <c>sim-7</c> file with the engine label replaced and, in the run record, the schema number and the key hash (both are in the key's pre-image) left out of the comparison: a strategy that names neither <c>days_to_results</c> nor <c>days_since_results</c> has the candidates, the
/// trades and the counters it had under sim-7. The goldens are regenerated on purpose with <c>XD_UPDATE_GOLDEN=1</c>; this test is what proves the diff is the label.
/// </summary>
public sealed class ResultsDaysDifferentialTests
{
    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", file));

    private static string Normalised(string text) =>
        string.Join('\n', text.Split('\n').Where(l => !l.StartsWith("run schema=", StringComparison.Ordinal)).Select(l => l.Replace("sim-7", "sim-8", StringComparison.Ordinal)));

    [Theory]
    [InlineData("costed")]
    [InlineData("run-record")]
    [InlineData("scanner")]
    [InlineData("simulator")]
    [InlineData("swing")]
    [InlineData("vwap-pullback")]
    [InlineData("walker")]
    public void A_golden_of_sim_8_equals_the_frozen_golden_of_sim_7_but_for_the_engine_label_and_the_schema_and_key_line(string name)
    {
        var frozen = Read(name + ".sim-7.frozen.txt");
        var current = Read(name + ".golden.txt");

        frozen.Split('\n')[0].ShouldBe("engine sim-7", "the frozen file keeps its sim-7 header");
        current.Split('\n')[0].ShouldBe("engine sim-8");
        Normalised(current).ShouldBe(Normalised(frozen));
    }

    [Fact]
    public void The_schema_and_key_line_of_the_run_record_is_the_only_line_the_comparison_leaves_out_and_it_did_change()
    {
        var frozen = Read("run-record.sim-7.frozen.txt").Split('\n').Single(l => l.StartsWith("run schema=", StringComparison.Ordinal));
        var current = Read("run-record.golden.txt").Split('\n').Single(l => l.StartsWith("run schema=", StringComparison.Ordinal));

        frozen.ShouldStartWith("run schema=7 key=");
        current.ShouldStartWith("run schema=8 key=");
        current.ShouldEndWith("engine=sim-8");
        current.ShouldNotBe(frozen);
    }
}
