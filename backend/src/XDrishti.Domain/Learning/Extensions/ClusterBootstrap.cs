using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The cluster bootstrap of a difference of two means (P15-10): whole blocks (days or seasons) are drawn with replacement, both groups together, so the dependence inside a block is kept. Seeded, so the same blocks give the same bounds. Pure.</summary>
public static class ClusterBootstrap
{
    /// <summary>The mean of A minus the mean of B over all blocks, with the one-sided lower bound at <paramref name="level"/> and the upper bound at the same level. All three are null when there are fewer than two blocks or a group is empty (unavailable).</summary>
    public static BootstrapBounds Difference(IReadOnlyList<DifferenceBlock> blocks, int draws, ulong seed, double level)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentOutOfRangeException.ThrowIfLessThan(draws, 1);
        var (sumA, countA, sumB, countB) = (0.0, 0, 0.0, 0);
        foreach (var b in blocks)
        {
            sumA += b.SumA;
            countA += b.CountA;
            sumB += b.SumB;
            countB += b.CountB;
        }

        if (countA == 0 || countB == 0)
        {
            return new BootstrapBounds(null, null, null, null, blocks.Count, draws);
        }

        var point = (sumA / countA) - (sumB / countB);
        if (blocks.Count < 2)
        {
            return new BootstrapBounds(point, null, null, null, blocks.Count, draws);
        }

        var random = new SeededRandom(seed);
        var values = new List<double>(draws);
        for (var d = 0; d < draws; d++)
        {
            var (a, na, b, nb) = (0.0, 0, 0.0, 0);
            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[random.Next(blocks.Count)];
                a += block.SumA;
                na += block.CountA;
                b += block.SumB;
                nb += block.CountB;
            }

            if (na > 0 && nb > 0)
            {
                values.Add((a / na) - (b / nb));
            }
        }

        if (values.Count == 0)
        {
            return new BootstrapBounds(point, null, null, null, blocks.Count, draws);
        }

        values.Sort();
        var mean = values.Sum() / values.Count;
        var square = values.Sum(v => (v - mean) * (v - mean));
        return new BootstrapBounds(point, values[BlockBootstrap.Position(1 - level, values.Count) - 1], values[BlockBootstrap.Position(level, values.Count) - 1], Math.Sqrt(square / Math.Max(1, values.Count - 1)), blocks.Count, draws);
    }
}
