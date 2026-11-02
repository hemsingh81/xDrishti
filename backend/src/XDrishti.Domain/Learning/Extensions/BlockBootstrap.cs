using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The paired block bootstrap of the comparison (P13-05). A draw resamples as many blocks as there are blocks, with replacement, each carrying all its rows' paired differences; the draw's value is the sum of the drawn differences over the rows drawn. The blocks are given in a
/// fixed order and drawn with <see cref="SeededRandom"/>, so a seed gives the same draws on every run. The one-sided bounds are order statistics: position <c>ceil((1 - level) x B)</c> and <c>ceil(level x B)</c>, 1-based (10,000 draws, 90 %: the 1,000th and the 9,000th).
/// </summary>
public static class BlockBootstrap
{
    /// <summary>The bounds of the mean paired difference.</summary>
    /// <param name="blocks">The blocks, already keyed and sorted by the caller.</param>
    /// <param name="draws">The number of draws.</param>
    /// <param name="seed">The generator seed.</param>
    /// <param name="level">The one-sided level, above 0.5 and below 1.</param>
    public static BootstrapBounds Bounds(IReadOnlyList<BootstrapBlock> blocks, int draws, ulong seed, double level)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentOutOfRangeException.ThrowIfLessThan(draws, 1);
        var rows = 0;
        var sum = 0.0;
        foreach (var block in blocks)
        {
            rows += block.Count;
            sum += block.Sum;
        }

        double? point = rows > 0 ? sum / rows : null;
        if (blocks.Count < 2 || rows == 0)
        {
            return new BootstrapBounds(point, null, null, null, blocks.Count, draws);
        }

        var random = new SeededRandom(seed);
        var values = new double[draws];
        for (var d = 0; d < draws; d++)
        {
            var s = 0.0;
            var n = 0;
            for (var i = 0; i < blocks.Count; i++)
            {
                var b = blocks[random.Next(blocks.Count)];
                s += b.Sum;
                n += b.Count;
            }

            values[d] = n > 0 ? s / n : 0;
        }

        var mean = 0.0;
        foreach (var v in values)
        {
            mean += v;
        }

        mean /= draws;
        var square = 0.0;
        foreach (var v in values)
        {
            square += (v - mean) * (v - mean);
        }

        Array.Sort(values);
        return new BootstrapBounds(point, values[Position(1 - level, draws) - 1], values[Position(level, draws) - 1], Math.Sqrt(square / Math.Max(1, draws - 1)), blocks.Count, draws);
    }

    /// <summary>The 1-based position <c>ceil(share x draws)</c>, kept inside 1 to draws.</summary>
    internal static int Position(double share, int draws) => Math.Clamp((int)Math.Ceiling(Math.Round(share * draws, 9)), 1, draws);
}
