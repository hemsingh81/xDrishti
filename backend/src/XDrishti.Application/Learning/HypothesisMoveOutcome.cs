namespace XDrishti.Application.Learning;

/// <summary>What a hypothesis command did.</summary>
/// <param name="Id">The hypothesis id.</param>
/// <param name="State">The state after the command.</param>
/// <param name="Text">A plain sentence, including what was not done.</param>
public sealed record HypothesisMoveOutcome(long Id, string State, string Text);
