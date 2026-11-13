using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Replay.HoldOut;

/// <summary>The target block of a registration (SR-03 D2): the earliest block of the chain whose start is after today. Pure.</summary>
public static class SystemTarget
{
    /// <summary>
    /// The earliest block that has not begun. The candidates are the stored blocks and the one block after the newest stored block (its start is the day after the newest block's end, known as soon as that block is stored). Null when none starts after
    /// <paramref name="today"/> (the India date): block 1 and a block that has begun are never a target.
    /// </summary>
    public static (DateOnly Start, DateOnly End)? Pick(IReadOnlyList<(DateOnly Start, DateOnly End)> storedBlocks, int blockMonths, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(storedBlocks);
        var candidates = storedBlocks.OrderBy(b => b.Start).ToList();
        if (candidates.Count > 0)
        {
            candidates.Add(HoldOutChain.Next(candidates[^1].End, blockMonths));
        }

        foreach (var block in candidates)
        {
            if (block.Start > today)
            {
                return block;
            }
        }

        return null;
    }
}
