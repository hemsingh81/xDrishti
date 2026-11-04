namespace XDrishti.Domain.Learning;

/// <summary>One closed trade as the cell statistics read it: the India date of its exit (when it became known), R in the unit of its cell, and the excursions in R.</summary>
/// <param name="ExitDate">India date of the exit; the trade counts at an as-of date on or after it, never before.</param>
/// <param name="R">The result in R (net of charges when the cell is costed, else from the filled prices).</param>
/// <param name="MaeR">Maximum adverse excursion in R, not below 0.</param>
/// <param name="MfeR">Maximum favourable excursion in R, not below 0.</param>
/// <param name="EntryDate">India session date of the entry (CF-01): the cluster of the trade. Null when the caller does not know it (an older call): the cluster is then the exit date, which is not the entry cluster.</param>
public sealed record CellTrade(DateOnly ExitDate, double R, double MaeR, double MfeR, DateOnly? EntryDate = null)
{
    /// <summary>The cluster of the trade: its India entry session date as text (the exit date when the entry date is not known).</summary>
    public string ClusterKey => (EntryDate ?? ExitDate).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>True when <see cref="ClusterKey"/> is the entry session (so a cluster estimate means what it says).</summary>
    public bool HasEntryCluster => EntryDate.HasValue;
}
