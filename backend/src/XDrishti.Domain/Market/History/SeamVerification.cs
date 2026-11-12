namespace XDrishti.Domain.Market.History;

/// <summary>What <see cref="SeamVerification.Decide"/> reads about one instrument. Every field is a fact the caller read; nothing is assumed.</summary>
/// <param name="SwitchOn"><c>Data:History:Replay:AdmitVerified</c>.</param>
/// <param name="ProbeStatus">The stored probe status (<see cref="ProbeStatus"/>).</param>
/// <param name="RecordedSeam">The seam the seam row holds (<c>seam_at</c>).</param>
/// <param name="CurrentSeam">The seam read now from the stored bars (the oldest CSV bar); null when the instrument has none any more.</param>
/// <param name="HighWater">The high-water mark of the seam (never lowered).</param>
/// <param name="UnfinishedChunks">The instrument has a <c>planned</c> or <c>failed</c> chunk.</param>
/// <param name="ContinuityBasis">The basis the continuity check last ran on; null when it never ran.</param>
/// <param name="SeamJump">The continuity check's verdict.</param>
/// <param name="ProbeBasis">The basis of the last passed probe; null after a failed or no probe.</param>
/// <param name="CurrentBasis"><see cref="SeamBasis.Of"/> of the facts now.</param>
/// <param name="DhanInCsvSpan">A <c>dhan</c> bar is stored between the seam date and the last in-span ex-date.</param>
public sealed record SeamVerificationInput(
    bool SwitchOn,
    string ProbeStatus,
    DateTimeOffset RecordedSeam,
    DateTimeOffset? CurrentSeam,
    DateTimeOffset HighWater,
    bool UnfinishedChunks,
    string? ContinuityBasis,
    bool SeamJump,
    string? ProbeBasis,
    string CurrentBasis,
    bool DhanInCsvSpan);

/// <summary>
/// Whether the pre-seam (Dhan, raw) bars of an instrument may enter a simulation (CF-04d decision 2). Verified means every check below holds; the first that fails is the reason, in this order, and anything unknown fails: <c>switch_off</c>, <c>not_probed</c>, <c>seam_unresolved</c>,
/// <c>seam_moved</c>, <c>backfill_unfinished</c>, <c>continuity_unchecked</c>, <c>seam_jump</c>, <c>basis_changed</c>, <c>dhan_in_csv_span</c>. Pure.
/// </summary>
public static class SeamVerification
{
    public const string SwitchOff = "switch_off";
    public const string NotProbed = "not_probed";
    public const string SeamUnresolved = "seam_unresolved";
    public const string SeamMoved = "seam_moved";
    public const string BackfillUnfinished = "backfill_unfinished";
    public const string ContinuityUnchecked = "continuity_unchecked";
    public const string SeamJump = "seam_jump";
    public const string BasisChanged = "basis_changed";
    public const string DhanInCsvSpan = "dhan_in_csv_span";

    /// <summary>The first failing reason, or null when the instrument is verified.</summary>
    public static string? Decide(SeamVerificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.SwitchOn)
        {
            return SwitchOff;
        }

        if (input.ProbeStatus != ProbeStatus.Passed)
        {
            return input.ProbeStatus == ProbeStatus.Unresolved ? SeamUnresolved : NotProbed;   // anything but a recorded pass, an unknown spelling included, is not a pass
        }

        if (input.CurrentSeam is not { } current || current != input.RecordedSeam || input.RecordedSeam != input.HighWater)
        {
            return SeamMoved;
        }

        if (input.UnfinishedChunks)
        {
            return BackfillUnfinished;
        }

        if (input.ContinuityBasis is null)
        {
            return ContinuityUnchecked;
        }

        if (input.SeamJump)
        {
            return SeamJump;
        }

        if (!string.Equals(input.ProbeBasis, input.CurrentBasis, StringComparison.Ordinal) || !string.Equals(input.ContinuityBasis, input.CurrentBasis, StringComparison.Ordinal))
        {
            return BasisChanged;
        }

        return input.DhanInCsvSpan ? DhanInCsvSpan : null;
    }
}
