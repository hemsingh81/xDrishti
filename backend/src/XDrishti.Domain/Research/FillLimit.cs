namespace XDrishti.Domain.Research;

/// <summary>One thing the fills do not model: a stable code and an English text.</summary>
public sealed record FillLimit(string Code, string Text);
