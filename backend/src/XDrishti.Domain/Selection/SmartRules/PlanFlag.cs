namespace XDrishti.Domain.Selection;

/// <summary>A visible flag on the plan (banner line).</summary>
/// <param name="Code">Stable code from <see cref="SmartRuleCodes"/>.</param>
/// <param name="Text">The words shown to the owner.</param>
public sealed record PlanFlag(string Code, string Text);
