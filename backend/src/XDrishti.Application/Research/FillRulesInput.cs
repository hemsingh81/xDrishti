namespace XDrishti.Application.Research;

/// <summary>
/// The fill rules of a request as raw numbers (spec P3-04a, D1): <c>FillRules</c> and <c>SlippageConfig</c> are valid by construction, so the handler builds them with their <c>Create</c> and the invalid case can exist and be reported
/// (<c>simulation.slippage_invalid</c>, <c>simulation.fill_rules_invalid</c>) before any bar is read.
/// </summary>
/// <param name="EntryLimitPercent">The limit of the entry order above the decision price, in percent (0 to 5).</param>
/// <param name="Ticks">Slippage of a market-type fill, in ticks (0 to 100).</param>
/// <param name="BarRangeFrac">Slippage as a share of the fill bar's range (0 to 1).</param>
/// <param name="StopExitExtraTicks">More ticks of slippage on a stop exit (0 to 100).</param>
public sealed record FillRulesInput(double EntryLimitPercent, double Ticks, double BarRangeFrac, double StopExitExtraTicks);
