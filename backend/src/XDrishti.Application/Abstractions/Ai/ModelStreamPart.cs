namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A piece of the model's streamed answer.</summary>
public abstract record ModelStreamPart;

/// <summary>Text produced by the model.</summary>
public sealed record ModelTextPart(string Text) : ModelStreamPart;

/// <summary>A tool call the model asks for (complete).</summary>
public sealed record ModelToolCallPart(ModelToolCall Call) : ModelStreamPart;

/// <summary>The end of the model's turn: why it stopped (<c>stop</c>, <c>length</c>, <c>tool_calls</c>, ...) and the tokens, when the server reported them.</summary>
public sealed record ModelFinishPart(string Reason, int? PromptTokens, int? CompletionTokens) : ModelStreamPart;
