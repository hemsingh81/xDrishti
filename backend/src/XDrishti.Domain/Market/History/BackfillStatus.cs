namespace XDrishti.Domain.Market.History;

/// <summary>The stored spelling of a chunk's state in <c>market.backfill_chunks</c>.</summary>
public static class ChunkStatus
{
    public const string Planned = "planned";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Empty = "empty";
}

/// <summary>The stored spelling of the overlap probe's outcome per instrument.</summary>
public static class ProbeStatus
{
    public const string None = "none";
    public const string Passed = "passed";
    public const string Unresolved = "unresolved";
}

/// <summary>The state of an instrument's backfill as the owner sees it (<c>xd data history</c>).</summary>
public static class BackfillStatus
{
    public const string None = "none";
    public const string Planned = "planned";
    public const string Partial = "partial";
    public const string Done = "done";
    public const string SeamUnresolved = "seam_unresolved";
    public const string SeamJump = "seam_jump";

    /// <summary>The unresolved probe and the jump outrank the chunk state; with no chunks nothing was planned. <paramref name="finished"/> counts chunks that are <c>done</c> or <c>empty</c>.</summary>
    public static string Of(int total, int finished, int failed, string probeStatus, bool seamJump)
    {
        if (probeStatus == ProbeStatus.Unresolved)
        {
            return SeamUnresolved;
        }

        if (seamJump)
        {
            return SeamJump;
        }

        if (total == 0)
        {
            return None;
        }

        if (finished == total)
        {
            return Done;
        }

        return finished == 0 && failed == 0 ? Planned : Partial;
    }
}
