using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>The daily run of the ML monitor (spec CF-10 D7 and D8, part 10d-1): every champion is evaluated once for today for rollback, and, when <c>ml.auto_promote</c> is ON, every challenger is considered for promotion. Idempotent per model and day.</summary>
public sealed record EvaluateMlModelsCommand : ICommand;

/// <summary>What a daily run did.</summary>
/// <param name="Champions">Champions looked at.</param>
/// <param name="Checked">Evaluated now.</param>
/// <param name="AlreadyDone">Already evaluated today.</param>
/// <param name="Proposed">Rollback proposals written now.</param>
/// <param name="RolledBackAutomatically">Proposals the automatic setting applied now (zero while <c>ml.auto_rollback</c> is OFF).</param>
/// <param name="PromotedAutomatically">Challengers the automatic setting promoted now (zero while <c>ml.auto_promote</c> is OFF, and zero while no source of challenger evidence exists).</param>
/// <param name="NotPromoted">Challengers considered and refused (a refusal is an answer, not a failure).</param>
/// <param name="Failed">Evaluations that failed or threw (nothing is recorded for them today, so the next run repeats them).</param>
public sealed record MlRunOutcome(int Champions, int Checked, int AlreadyDone, int Proposed, int RolledBackAutomatically, int PromotedAutomatically, int NotPromoted, int Failed)
{
    /// <summary>One line for the service log.</summary>
    public string Summary => $"{Champions} champion(s), {Checked} evaluated, {AlreadyDone} already done today, {Proposed} rollback proposal(s), {RolledBackAutomatically} rolled back automatically, {PromotedAutomatically} promoted automatically ({NotPromoted} challengers not promoted), {Failed} failed";
}
