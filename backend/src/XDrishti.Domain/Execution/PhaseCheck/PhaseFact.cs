namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary><see cref="State"/> is <c>value</c>, <c>empty</c>, <c>insufficient</c> or <c>unavailable</c>; the text carries the n. Printed beside the verdict, never part of it.</summary>
public sealed record PhaseFact(string Name, string State, string Text);
