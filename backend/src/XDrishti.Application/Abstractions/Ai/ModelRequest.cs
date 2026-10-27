namespace XDrishti.Application.Abstractions.Ai;

/// <summary>One model request: the messages, the tools on offer (declarations only; the model server never runs them), and the sampling limits.</summary>
public sealed record ModelRequest(IReadOnlyList<ModelMessage> Messages, IReadOnlyList<ToolDescriptor> Tools, int MaxOutputTokens, float Temperature, long? Seed);
