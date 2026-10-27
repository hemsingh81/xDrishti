namespace XDrishti.Application.Ai;

/// <summary>How a turn ended: the stored message, the answer text, the grounding, the number of tool calls, the tokens, and why it ended (<c>stop</c>, <c>length</c>, <c>tool_limit</c>, <c>cancelled</c>, <c>error</c>).</summary>
public sealed record TurnResult(long MessageId, string Answer, TurnGrounding Grounding, int ToolCalls, TurnUsage Usage, string FinishReason, int? FirstTokenMs, int TotalMs);
