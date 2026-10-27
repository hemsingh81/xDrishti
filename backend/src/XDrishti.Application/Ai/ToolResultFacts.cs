namespace XDrishti.Application.Ai;

/// <summary>What the envelope text of a tool result says (spec P9-00 envelope), read without trusting it: <paramref name="Parsed"/> is false when the text is not a JSON object.</summary>
public sealed record ToolResultFacts(bool Parsed, bool Ok, string? ErrorCode, string? ErrorMessage, int Rows, bool Truncated, string? EvidenceJson, TurnEvidence? Evidence, int Redactions, int InjectionFlags);
