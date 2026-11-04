namespace XDrishti.Application.OptionReports;

/// <summary>
/// The newest stored record of one P14 family (spec P14-16): <c>stored</c> with the verdict, the folds, the trials, the named missing quantities and the stated limits, <c>none stored</c>, or <c>cut</c>. No estimate and no figure of return: the evidence block
/// of a record is read with <c>GET /api/research/extensions/{id}</c>.
/// </summary>
public sealed record P14VerdictItem(string Family, string State, string? Id, string? Protocol, string? Verdict, int? Trials, int? FoldsEvaluable, int? FoldsTotal, bool? HoldOutRead, IReadOnlyList<string> Missing, IReadOnlyList<string> Limits);

/// <summary>Every P14 family that exists, in a fixed order.</summary>
public sealed record P14VerdictList(IReadOnlyList<P14VerdictItem> Families);
