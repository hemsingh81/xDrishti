using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// One choice of the parameters of a <c>PYRAMID</c> derived from a seed, so that every shape of D13 occurs: four ladders (two to four levels, the first as low as a quarter of a risk so that a close beyond two levels happens), a
/// <c>max_adds</c> below the ladder, four ATR multiples, a full size that is unknown (a gross run) or small enough that an add holds no whole share, and the <c>time_stop</c> overlay on every third seed.
/// </summary>
internal sealed record PyramidVariant(decimal Initial, decimal[] Levels, decimal[] Shares, int MaxAdds, decimal Mult, int Period, long? FullSize, decimal? MinProgress, int Bars, int Sessions)
{
    private static readonly (decimal Initial, decimal[] Levels, decimal[] Shares)[] Ladders =
    [
        (0.5m, [1m, 2m], [0.3m, 0.2m]),
        (0.4m, [0.5m, 1m, 1.5m], [0.2m, 0.2m, 0.2m]),
        (0.25m, [0.25m, 0.5m, 0.75m, 1m], [0.25m, 0.25m, 0.125m, 0.125m]),
        (0.6m, [0.5m], [0.4m]),
    ];

    private static readonly decimal[] Mults = [1.0m, 2.0m, 0.5m, 3.5m];

    private static readonly int[] Periods = [1, 3, 5, 14];

    private static readonly long?[] Sizes = [null, 3, 5, 100, null, 40];

    private static readonly decimal[] Minimums = [0m, 0.5m, 1.0m];

    private static readonly int[] BarCounts = [1, 2, 3, 4, 6];

    internal static PyramidVariant Of(long seed, int holdSessions)
    {
        var k = (int)(Math.Abs(seed) % 1_000_003);
        var ladder = Ladders[k % Ladders.Length];
        var maxAdds = 1 + ((k / 4) % ladder.Levels.Length);
        decimal? minimum = null;
        var bars = 0;
        var sessions = 0;
        if (k % 3 == 0)
        {
            if (holdSessions == 0)
            {
                minimum = Minimums[(k / 5) % Minimums.Length];
                bars = BarCounts[(k / 7) % BarCounts.Length];
            }
            else if (holdSessions >= 2)
            {
                minimum = Minimums[(k / 5) % Minimums.Length];
                sessions = 1 + ((k / 7) % (holdSessions - 1));
            }
        }

        return new PyramidVariant(ladder.Initial, ladder.Levels, ladder.Shares, maxAdds, Mults[(k / 3) % Mults.Length], Periods[(k / 11) % Periods.Length], Sizes[(k / 13) % Sizes.Length], minimum, bars, sessions);
    }

    internal PyramidPlan Plan() => new(Initial, Levels, Shares, MaxAdds);

    internal LegPolicy Engine() => new(PartialKind.None, 0m, 0m, 0m, MinProgress, Bars, Sessions, Plan());
}

/// <summary>
/// The tape and the rules of a <c>PYRAMID</c> for a list of minutes, in the two forms the P4-04 tests need: the engine's <see cref="ExitContext"/> (the chandelier of the combined position as <see cref="AtrTrailRule"/> with no activation, the
/// policy with the ladder, the full size) and the oracle's <see cref="OracleTrail"/>, <see cref="OraclePolicy"/> and <see cref="OraclePyramid"/>. The bars come from <see cref="TrailTape"/>, so the two forms read one input.
/// </summary>
internal static class PyramidTape
{
    internal sealed record Built(ExitContext Context, OracleTrail Trail, OraclePyramid Pyramid, OraclePolicy? Policy, ExitTape Tape);

    internal static Built Of(IReadOnlyList<Bar> minutes, Side side, PyramidVariant variant, int bucketMinutes)
    {
        var trail = TrailTape.Of(minutes, OracleTrailKind.Atr, side, variant.Period, (double)variant.Mult, 0, bucketMinutes);
        var oracleTrail = trail.Trail with { Multiple = (double)variant.Mult, ActivateAtRr = 0 };
        var full = variant.FullSize;
        var context = new ExitContext(new AtrTrailRule(0, variant.Mult, 0m), trail.Tape, variant.Engine(), full is { } q ? (_, _) => q : null);
        var pyramid = new OraclePyramid(variant.Initial, variant.Levels, variant.Shares, variant.MaxAdds, oracleTrail.Bars, full);
        var policy = variant.MinProgress is null
            ? null
            : new OraclePolicy(OraclePolicyKind.None, 0m, 0m, 0m, variant.MinProgress, variant.Bars, variant.Sessions, [.. oracleTrail.Bars.Select(b => new OraclePolicyBar(b.CloseEpochSeconds, b.Close, b.Value))]);
        return new Built(context, oracleTrail, pyramid, policy, trail.Tape);
    }
}
