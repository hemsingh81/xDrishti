namespace XDrishti.Application.Learning;

/// <summary>The newest record of one kind, as the overview shows it (P6-18): what it is, the verdict that was stored and one line of the stored numbers. Nothing here is computed.</summary>
/// <param name="Id">The record id.</param>
/// <param name="Label">Role of a model, knob of an experiment, kind of a changelog row.</param>
/// <param name="Verdict">The stored verdict, outcome or state after.</param>
/// <param name="Detail">One line of the stored numbers.</param>
/// <param name="At">When it was recorded.</param>
public sealed record OverviewHeadline(long Id, string Label, string Verdict, string Detail, DateTimeOffset At);
