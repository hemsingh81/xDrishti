namespace XDrishti.Domain.Market.History;

/// <summary>What a backfill will cost, estimated before it starts (CF-04 Part B, decisions 7 and 12). The figures are estimates, and the times are minimums: retries, back-off after a rate limit and the run window add to them.</summary>
public readonly record struct BackfillEstimate(long Rows, long Requests, double MinutesAtRate)
{
    public const int SessionsPerYear = 250;

    public const int MinutesPerSession = 375;

    /// <summary>Rows of one instrument for a gap of <paramref name="gapYears"/> years: sessions (250 a year) times 375 minutes.</summary>
    public static long RowsOfGap(double gapYears) => (long)Math.Round(gapYears * SessionsPerYear, MidpointRounding.AwayFromZero) * MinutesPerSession;

    /// <summary>Rows of a gap of whole calendar days (the planner's chunks): the same 250-sessions-a-year rule.</summary>
    public static long RowsOfDays(int days) => (long)Math.Round(days * (double)SessionsPerYear / 365, MidpointRounding.AwayFromZero) * MinutesPerSession;

    /// <summary>Equal instruments: <paramref name="instruments"/> of an average gap, <paramref name="requestsPerInstrument"/> requests each, at <paramref name="requestsPerSecond"/>.</summary>
    public static BackfillEstimate ForEqual(int instruments, double gapYears, int requestsPerInstrument, double requestsPerSecond) =>
        Of(instruments * RowsOfGap(gapYears), (long)instruments * requestsPerInstrument, requestsPerSecond);

    public static BackfillEstimate Of(long rows, long requests, double requestsPerSecond) =>
        new(rows, requests, requestsPerSecond > 0 ? requests / requestsPerSecond / 60.0 : 0);
}

/// <summary>The disk guard (decision 12): the backfill refuses to start when the free space is under <c>factor</c> times the estimated bytes.</summary>
public static class BackfillDiskGuard
{
    /// <summary>Estimated bytes of <paramref name="rows"/> at <paramref name="bytesPerRow"/> (an upper-bound assumption).</summary>
    public static long BytesOf(long rows, int bytesPerRow) => checked(rows * bytesPerRow);

    /// <summary>The free space the run needs: the estimated bytes times <paramref name="headroomFactor"/>.</summary>
    public static long RequiredBytes(long rows, int bytesPerRow, int headroomFactor) => checked(BytesOf(rows, bytesPerRow) * headroomFactor);

    /// <summary>True when there is enough room. Unknown free space (null) is not enough.</summary>
    public static bool Enough(long? freeBytes, long rows, int bytesPerRow, int headroomFactor) =>
        freeBytes is { } free && free >= RequiredBytes(rows, bytesPerRow, headroomFactor);
}
