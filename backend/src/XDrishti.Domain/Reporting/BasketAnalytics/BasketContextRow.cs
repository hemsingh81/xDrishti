namespace XDrishti.Domain.Reporting;

/// <summary>
/// One day of a basket's context (spec BK-08, criterion 7): breadth, dispersion and average correlation of the members as of the day, stored once and never updated. A figure that is not a number is null; <see cref="State"/> is <c>value</c> or <c>insufficient</c> (too few voters for the 20-day breadth).
/// </summary>
/// <param name="BasketId">The basket.</param>
/// <param name="AsOf">The India session the row describes; it uses closes up to this day and the members as of it.</param>
/// <param name="NMembers">The members as of the day.</param>
/// <param name="NVoters">The members that voted in the 20-day breadth (a bar on the day and 20 closes).</param>
/// <param name="B20">Share (0 to 1) of voters above their 20-day average; the figure the gate reads. Null unless <paramref name="State"/> is <c>value</c>.</param>
/// <param name="B50">The same for the 50-day average.</param>
/// <param name="B200">The same for the 200-day average.</param>
/// <param name="AdShare">Advancers over advancers plus decliners.</param>
/// <param name="NewHighShare">Share at a 252-session high.</param>
/// <param name="Dispersion">Cross-sectional standard deviation of the day's returns.</param>
/// <param name="RhoBar">Average pairwise correlation over the last 60 sessions.</param>
/// <param name="State"><c>value</c> or <c>insufficient</c>.</param>
/// <param name="MembershipBasis"><c>point_in_time</c> (dated membership) or <c>single_snapshot</c> (today's members taken back; never presented as point in time).</param>
public sealed record BasketContextRow(Guid BasketId, DateOnly AsOf, int NMembers, int NVoters, double? B20, double? B50, double? B200, double? AdShare, double? NewHighShare, double? Dispersion, double? RhoBar, string State, string MembershipBasis)
{
    /// <summary>The 20-day breadth has enough voters.</summary>
    public const string ValueState = "value";

    /// <summary>Too few voters: the 20-day breadth is not a number.</summary>
    public const string InsufficientState = "insufficient";

    /// <summary>The members are those of a dated membership.</summary>
    public const string PointInTime = "point_in_time";

    /// <summary>The members are one snapshot taken back in time.</summary>
    public const string SingleSnapshot = "single_snapshot";
}
