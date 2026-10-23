namespace XDrishti.Domain.Planning;

/// <summary>
/// The grade of one ticket (P7-13, ADR 0090): what the simulator's rules would have returned on the stored bars of the planned session. It is never a fill: the label is always <see cref="Basis"/>.
/// </summary>
/// <param name="Status">Graded, not triggered or not gradable.</param>
/// <param name="R">Result in R: after the charge sheet when the replay was costed, from the filled prices when gross. Only when graded.</param>
/// <param name="MaeR">Maximum adverse excursion in R. Only when graded.</param>
/// <param name="MfeR">Maximum favourable excursion in R. Only when graded.</param>
/// <param name="ExitReason">Why the simulated trade ended (stop, target, square-off, max hold ...). Only when graded.</param>
/// <param name="EntryPrice">The exact simulated entry. Only when graded.</param>
/// <param name="ExitPrice">The exact simulated exit. Only when graded.</param>
/// <param name="CostBasis">What R is net of: <c>net of the charge sheet X</c> or <c>gross, no charges</c>.</param>
/// <param name="Detail">Why: the reason a ticket is not gradable, what did not happen for a ticket not triggered, or the entry and exit session for a graded one.</param>
public sealed record TicketGrade(GradeStatus Status, double? R, double? MaeR, double? MfeR, string? ExitReason, double? EntryPrice, double? ExitPrice, string CostBasis, string Detail)
{
    /// <summary>The label every grade carries: the grade is a re-simulation on plan prices, not an execution.</summary>
    public const string Basis = "simulated on plan prices";

    /// <summary>Whether this ticket contributes an R to a sum.</summary>
    public bool HasR => Status == GradeStatus.Graded && R is not null;
}
