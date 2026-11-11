namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>One stored trade of the sizing study: its net R and the scale the rule would have given at its decision date.</summary>
/// <param name="NetR">Net R multiple of the stored trade (before this study's scale).</param>
/// <param name="Scale">The scale, 0 to 1 (a value above 1 is read as 1: the study has no up-scaling).</param>
public sealed record StudyTrade(double NetR, double Scale);
