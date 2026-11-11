namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A basket the assistant may name: its id and its name (the name is untrusted text).</summary>
public sealed record AiBasketRef(Guid Id, string Name);
