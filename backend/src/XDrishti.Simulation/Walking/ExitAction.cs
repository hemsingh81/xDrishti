namespace XDrishti.Simulation.Walking;

/// <summary>One request of an exit rule (P4-01, D5): a level for <see cref="ExitActionKind.MoveStop"/> and <see cref="ExitActionKind.MoveTarget"/>, a share for <see cref="ExitActionKind.ExitShare"/> with the code that names the leg.</summary>
internal readonly record struct ExitAction(ExitActionKind Kind, decimal Value, string Code = "");
