namespace XDrishti.Domain.Reporting;

/// <summary>The relative strength line and returns of a basket (BK-06 m8).</summary>
/// <param name="Line">RS rebased to 100 at the first common date.</param>
/// <param name="Returns">One per window; RS(last) / RS(last - w) - 1 as a fraction.</param>
public sealed record RelativeStrengthResult(IReadOnlyList<RelativeStrengthPoint> Line, IReadOnlyList<RelativeStrengthReturn> Returns);
