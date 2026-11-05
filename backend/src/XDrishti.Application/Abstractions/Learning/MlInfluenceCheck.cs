using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>One evaluation of the influence predicate for a model, as <c>research.ml_influence_checks</c> keeps it (CF-10 part 10c-2). Insert-only.</summary>
/// <param name="ModelId">The registry id.</param>
/// <param name="EvaluatedAt">When.</param>
/// <param name="MayInfluence">The answer.</param>
/// <param name="Terms">The four terms with their reasons (and a <c>not_champion</c> term when the model is not the champion).</param>
/// <param name="SettingHash">SHA-256 of the thresholds and of the switch as they were.</param>
/// <param name="InputsHash">SHA-256 of the answer, the terms and the setting hash: equal for equal evaluations.</param>
public sealed record MlInfluenceCheck(long ModelId, DateTimeOffset EvaluatedAt, bool MayInfluence, IReadOnlyList<MlTermResult> Terms, string SettingHash, string InputsHash);
