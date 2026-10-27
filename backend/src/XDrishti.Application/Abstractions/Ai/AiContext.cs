namespace XDrishti.Application.Abstractions.Ai;

/// <summary>What the database says the assistant may do right now (spec P9-02): the newest row of <c>ai.settings</c>; no row means both are false.</summary>
public sealed record AiContext(bool AssistantEnabled, bool PortfolioAllowed);
