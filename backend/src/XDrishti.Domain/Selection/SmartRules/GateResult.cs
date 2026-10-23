namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>The market gate's answer with its numbers (spec P7-06).</summary>
/// <param name="Mode">The mode.</param>
/// <param name="Status">Applied, unavailable or off.</param>
/// <param name="Code">Stable code.</param>
/// <param name="Vix">The VIX used, if known.</param>
/// <param name="Breadth">The breadth used, if known.</param>
/// <param name="MissingInputs">Inputs that were not known (a mode can still be reached from the others when one of them already trips a limit).</param>
/// <param name="Triggers">The limits that tripped, with numbers.</param>
public sealed record GateResult(GateMode Mode, RuleStatus Status, string Code, double? Vix, double? Breadth, IReadOnlyList<string> MissingInputs, IReadOnlyList<string> Triggers);
