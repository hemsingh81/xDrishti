using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Confidence;

/// <summary>The daily run of the decay monitor (spec CF-12, part 12b): every strategy with a shadow edge candidate in the current paper epoch is evaluated once for today. Idempotent per strategy and day.</summary>
public sealed record EvaluateDecayCommand : ICommand;

/// <summary>What a daily run did.</summary>
/// <param name="Strategies">Strategies looked at.</param>
/// <param name="Checked">Evaluated now.</param>
/// <param name="AlreadyDone">Already evaluated today.</param>
/// <param name="Proposed">Proposals written now.</param>
/// <param name="AppliedAutomatically">Proposals the automatic setting applied now (zero while it is OFF).</param>
/// <param name="Failed">Strategies whose evaluation failed (nothing is recorded for them today, so the next run repeats them).</param>
public sealed record DecayRunOutcome(int Strategies, int Checked, int AlreadyDone, int Proposed, int AppliedAutomatically, int Failed)
{
    /// <summary>One line for the service log.</summary>
    public string Summary => $"{Strategies} strategies, {Checked} evaluated, {AlreadyDone} already done today, {Proposed} proposal(s), {AppliedAutomatically} applied automatically, {Failed} failed";
}
