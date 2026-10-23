using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The triggers found.</summary>
/// <param name="Triggers">New-trades triggers, then drift triggers.</param>
/// <param name="OpenHypotheses">How many hypotheses are open.</param>
/// <param name="MaxOpen">The limit.</param>
/// <param name="Notes">What was and was not evaluated.</param>
public sealed record HypothesisTriggersView(IReadOnlyList<HypothesisTrigger> Triggers, int OpenHypotheses, int MaxOpen, IReadOnlyList<string> Notes);
