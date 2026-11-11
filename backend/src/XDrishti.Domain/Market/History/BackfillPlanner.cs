namespace XDrishti.Domain.Market.History;

/// <summary>
/// The chunks of one instrument's backfill (CF-04 Part B, decisions 6 and 7). The window is <c>[today - years, seam)</c> in India dates, half-open, where the seam is the date of the instrument's first CSV bar: nothing at or after
/// it is ever asked for here, because a Dhan bar would replace the owner's adjusted CSV bar of the same minute. The window is cut into chunks of at most <c>chunkDays</c> days, newest first (the chunks next to the CSV matter most if the run is
/// stopped early); the oldest chunk is the short one. Pure.
/// </summary>
public static class BackfillPlanner
{
    public const int MaxChunkDays = 90;

    /// <summary>The first date asked for: <paramref name="years"/> calendar years before <paramref name="today"/>.</summary>
    public static DateOnly StartOf(DateOnly today, int years) => today.AddYears(-years);

    /// <summary>The chunks, newest first, tiling <c>[start, seam)</c> exactly with no gap and no overlap. Empty when the seam is not after the start.</summary>
    public static IReadOnlyList<BackfillChunk> Chunks(DateOnly seam, DateOnly today, int years, int chunkDays)
    {
        if (years is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(years), "Years is 1 to 5.");
        }

        if (chunkDays is < 7 or > MaxChunkDays)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkDays), "ChunkDays is 7 to 90.");
        }

        var start = StartOf(today, years);
        var chunks = new List<BackfillChunk>();
        var to = seam;
        while (to > start)
        {
            var from = to.AddDays(-chunkDays);
            if (from < start)
            {
                from = start;
            }

            chunks.Add(new BackfillChunk(from, to));
            to = from;
        }

        return chunks;
    }
}
