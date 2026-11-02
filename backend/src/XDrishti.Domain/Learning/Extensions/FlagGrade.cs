namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The grade of the LLM announcement flag as the grader of P15-08 stored it on the newest accepted eval run (set D). Read, never recomputed (P15-10, decision 4).</summary>
/// <param name="EvalRunId">The eval run.</param>
/// <param name="Fingerprint">Its fingerprint.</param>
/// <param name="Labels">Labelled rows graded.</param>
/// <param name="Gating">Whether the run is a gating run.</param>
/// <param name="Passed">Whether set D passed.</param>
/// <param name="ModelHash">The model hash of the graded run, or null.</param>
/// <param name="PromptHash">The prompt hash, or null.</param>
public sealed record FlagGrade(string EvalRunId, string Fingerprint, int Labels, bool Gating, bool Passed, string? ModelHash, string? PromptHash);
