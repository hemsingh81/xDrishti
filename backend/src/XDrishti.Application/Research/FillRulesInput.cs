namespace XDrishti.Application.Research;

/// <summary>
/// The fill rules of a request as raw numbers (spec P3-04a, D1): <c>FillRules</c> and <c>SlippageConfig</c> are valid by construction, so the handler builds them with their <c>Create</c> and the invalid case can exist and be reported
/// (<c>simulation.slippage_invalid</c>, <c>simulation.fill_rules_invalid</c>) before any bar is read.
/// </summary>
/// <param name="EntryLimitPercent">The limit of the entry order above the decision price, in percent (0 to 5).</param>
/// <param name="Ticks">Slippage of a market-type fill, in ticks (0 to 100).</param>
/// <param name="BarRangeFrac">Slippage as a share of the fill bar's range (0 to 1).</param>
/// <param name="StopExitExtraTicks">More ticks of slippage on a stop exit (0 to 100).</param>
/// <param name="EntryDelayMinutes">Minutes the entry fill is delayed (CF-05, 0 to 5; 0 is the rule of P3-03b and leaves the run's fill text and key as they were).</param>
/// <param name="ConservativeSlippage">Slippage floors by liquidity tier, tier C and unavailable excluded and counted (CF-05, D2, ADR 0229); false is the rule of P3-03b and leaves the run text and key as they were.</param>
public sealed record FillRulesInput(double EntryLimitPercent, double Ticks, double BarRangeFrac, double StopExitExtraTicks, int EntryDelayMinutes = 0, bool ConservativeSlippage = false);
