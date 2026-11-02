namespace XDrishti.Application.Learning;

/// <summary>One verdict as the verb prints it. <see cref="Stored"/> is false when the same record was stored already (a re-run writes nothing).</summary>
/// <param name="EvaluationId">The record id (SHA-256 of its inputs and result).</param>
/// <param name="Protocol">F, X, P or L.</param>
/// <param name="VersionKey">What was evaluated, for example <c>evf-1:N=2</c>.</param>
/// <param name="Verdict">KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE.</param>
/// <param name="Missing">The named missing quantities.</param>
/// <param name="Stored">Whether the record is new.</param>
/// <param name="HoldOutRead">Whether the record is the reading of the hold-out.</param>
public sealed record EvaluatedRecord(string EvaluationId, string Protocol, string VersionKey, string Verdict, IReadOnlyList<string> Missing, bool Stored, bool HoldOutRead);
