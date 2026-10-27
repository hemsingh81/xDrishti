namespace XDrishti.Domain.Assistant.Drafting;

/// <summary>One thing a check found wrong with a draft: a code and the plain detail (a path or a number; never the idea text).</summary>
public sealed record DraftFinding(string Code, string Detail);
