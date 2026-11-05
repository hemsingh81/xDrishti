namespace XDrishti.Domain.Learning;

/// <summary>
/// What one kill rule says, with the numbers (spec CF-12, D2; ADR 0218). An evaluation only: it proposes, persists and executes nothing (that is part 12b).
/// </summary>
/// <param name="Rule">A stable code: <c>cost_stress</c>, <c>drawdown</c>, <c>decay_twice</c> or <c>failure_boundary</c>.</param>
/// <param name="State">The reading.</param>
/// <param name="Value">The figure the rule judged (net R at 1.5 times cost, the drawdown in R, the streak, the sequential sum); null when not evaluated.</param>
/// <param name="Limit">What it was judged against (0, the factor times the 95th percentile, 2, the failure boundary); null when not evaluated.</param>
/// <param name="Trades">Trades the figure is based on.</param>
/// <param name="ProposalOnly">True for the CF-09 failure boundary: it already hard-fails the scorecard, so it may only ever create a proposal and the auto setting (12b) never executes it.</param>
/// <param name="Text">The words, with the numbers.</param>
public sealed record KillRuleVerdict(string Rule, KillRuleState State, double? Value, double? Limit, int Trades, bool ProposalOnly, string Text);
