namespace XDrishti.Domain.Assistant.Text;

/// <summary>One thing the guard refused: a stable <paramref name="Code"/>, the offending <paramref name="Token"/> (empty when it is a property of the whole text) and where it starts.</summary>
public sealed record GuardFinding(string Code, string Token, int Offset);
