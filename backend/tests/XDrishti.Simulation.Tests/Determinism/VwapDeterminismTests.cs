using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Determinism;

/// <summary>
/// Criterion 15 of P3-05: same input, same bytes. V1 to V7 and ten seeded days of the generator of D12, run twice (the second time on a copy of the bars), render identical text for the candidates with their traces, the trades and the counters of both
/// stages; the rendering equals its own golden file <c>vwap-pullback.golden.txt</c> (first line: the engine version; regenerated on purpose with <c>XD_UPDATE_GOLDEN=1</c>). The file is separate from <c>simulator.golden.txt</c> so that the ORB golden files
/// stay byte-identical (criterion 12).
/// </summary>
public sealed class VwapDeterminismTests
{
    private sealed record Case(string Name, StrategyDefinition Definition, Bar[] Bars);

    private static IEnumerable<Case> Cases()
    {
        var standard = VwapPullback.Create();
        yield return new Case("V1", standard, VwapDays.V1());
        yield return new Case("V2", standard, VwapDays.V2());
        yield return new Case("V3", standard, VwapDays.V3());
        yield return new Case("V4 pull 0.5", standard, VwapDays.V4());
        yield return new Case("V4 pull 2.0", VwapPullback.Create(2.0), VwapDays.V4());
        yield return new Case("V4 one bar later pull 2.0", VwapPullback.Create(2.0), VwapDays.V4(extraFlatBars: 1));
        yield return new Case("V5", standard, VwapDays.V5());
        yield return new Case("V6", standard, VwapDays.V6());
        yield return new Case("V7 all day", VwapDays.AllDay(), VwapDays.V7());
        for (ulong seed = 1; seed <= 10; seed++)
        {
            yield return new Case("seed " + seed, VwapPullback.Create(VwapSeeded.PullAtr(seed)), VwapSeeded.Of(seed));
        }
    }

    private static string RenderAll(bool copyBars)
    {
        var text = new StringBuilder();
        foreach (var c in Cases())
        {
            text.Append("== ").Append(c.Name).Append(" ==\n");
            text.Append(Render.Result(Sim.Run(c.Definition, copyBars ? (Bar[])c.Bars.Clone() : c.Bars)));
        }

        return text.ToString();
    }

    [Fact]
    public void The_hand_days_and_the_seeded_days_render_the_same_text_twice_the_second_time_on_a_copy_of_the_bars()
    {
        RenderAll(copyBars: false).ShouldBe(RenderAll(copyBars: true));
    }

    [Fact]
    public void The_rendering_equals_the_golden_file()
    {
        GoldenFile.Check("vwap-pullback", RenderAll(copyBars: false));
    }
}
