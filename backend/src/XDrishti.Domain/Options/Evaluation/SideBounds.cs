namespace XDrishti.Domain.Options.Evaluation;

/// <summary>The first and last entry date of the closed trades of one option side of a stored cash run (any fate but open at the end). Dates only: no figure.</summary>
public sealed record SideBounds(string Side, DateOnly First, DateOnly Last);
