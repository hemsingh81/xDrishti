namespace XDrishti.Domain.Market.History;

/// <summary>
/// The seam continuity check (decision 10): the adjusted close of the last pre-seam session against the first CSV open (also adjusted, both on today's basis). A move beyond ±<see cref="Threshold"/> is a <c>seam_jump</c>. The adjustment already contains every
/// applied action, so a jump left over is one no applied action explains (a demerger, a rights issue, a capital reduction): it is flagged, never repaired by the observed jump (that would hide a real move). Pure.
/// </summary>
public static class SeamContinuity
{
    /// <summary>The ISS-20 threshold: 20 %.</summary>
    public const double Threshold = 0.20;

    /// <summary>The move from the last pre-seam close to the first CSV open, as a fraction (0.0084 = +0.84 %). Zero when the close is not positive.</summary>
    public static double Move(double lastPreSeamAdjustedClose, double firstCsvAdjustedOpen) =>
        lastPreSeamAdjustedClose > 0 ? (firstCsvAdjustedOpen - lastPreSeamAdjustedClose) / lastPreSeamAdjustedClose : 0;

    /// <summary>True when the move is beyond ±20 % (exactly 20 % passes).</summary>
    public static bool IsJump(double lastPreSeamAdjustedClose, double firstCsvAdjustedOpen) =>
        Math.Abs(Move(lastPreSeamAdjustedClose, firstCsvAdjustedOpen)) > Threshold + 1e-12;
}
