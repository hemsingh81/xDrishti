using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Shadow;

/// <summary>
/// The graded shadow candidates that a model scored (<c>paper.shadow_scores</c> joined to <c>shadow_entries</c> and <c>shadow_grades</c>; CF-10e part 10e-1): only candidates that count for edge and whose grade is <c>graded</c>. Read only. Named by the learning evidence sources
/// alone; no Planning, Selection or Autonomy type reads it.
/// </summary>
public interface IShadowScoreReader
{
    /// <summary>The model's scored, graded candidates, oldest session first.</summary>
    ValueTask<IReadOnlyList<ScoredOutcome>> OutcomesAsync(long modelId, CancellationToken cancellationToken);
}
