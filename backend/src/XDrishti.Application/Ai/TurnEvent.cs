namespace XDrishti.Application.Ai;

/// <summary>An event of a streamed turn (spec P9-03 contract). <see cref="EventName"/> is the event name on the wire; the rest is the data (camelCase JSON).</summary>
public abstract record TurnEvent
{
    /// <summary>The event name on the wire. A method, so that it is never part of the data.</summary>
    public abstract string EventName();
}

/// <summary>The model that answers: the alias and, for a pinned file, its SHA-256.</summary>
public sealed record TurnModel(string Id, string? Sha256);

/// <summary>The first event: the ids of the turn, the prompt hash, the model and the settings row in force.</summary>
public sealed record TurnStarted(Guid ConversationId, Guid TurnId, long UserMessageId, string PromptHash, TurnModel Model, long? SettingsId) : TurnEvent
{
    public override string EventName() => "turn";
}

/// <summary>What the turn is doing: <c>reading</c> (the model reads the question), <c>tool</c> (a tool runs) or <c>answering</c> (the model writes the answer after the tools).</summary>
public sealed record TurnStatus(string Phase, string? Tool) : TurnEvent
{
    public override string EventName() => "status";
}

/// <summary>The model asked for a tool: the call number (from 1), the tool and the arguments as JSON text.</summary>
public sealed record TurnToolCall(int CallIndex, string Tool, System.Text.Json.JsonElement Arguments) : TurnEvent
{
    public override string EventName() => "tool_call";
}

/// <summary>What the basis of a result is (from its evidence).</summary>
public sealed record TurnEvidence(string? Basis, int? N, string? DateRange);

/// <summary>The outcome of a tool call: its stored id, how it ended (<c>ok</c>, <c>error</c>, <c>timeout</c>, <c>refused</c>, <c>disabled</c>), the row count, whether the text was cut, the time and the evidence.</summary>
public sealed record TurnToolResult(int CallIndex, long ToolCallId, string Status, int RowCount, bool Truncated, int DurationMs, TurnEvidence? Evidence, string? Error) : TurnEvent
{
    public override string EventName() => "tool_result";
}

/// <summary>A piece of the answer text of iteration <paramref name="Iteration"/>.</summary>
public sealed record TurnToken(int Iteration, string Text) : TurnEvent
{
    public override string EventName() => "token";
}

/// <summary>The text of this iteration was a preface to a tool call: the client drops it.</summary>
public sealed record TurnIterationDiscard(int Iteration) : TurnEvent
{
    public override string EventName() => "iteration_discard";
}

/// <summary>Nothing happened for a while; sent by the stream, not by the turn.</summary>
public sealed record TurnPing : TurnEvent
{
    public override string EventName() => "ping";
}

/// <summary>The grounding check as sent: the status code, how many figures were checked and the ones not found.</summary>
public sealed record TurnGrounding(string Status, int Checked, IReadOnlyList<Domain.Assistant.Grounding.UngroundedSpan> Ungrounded);

/// <summary>Tokens of the turn (null when the server did not report them).</summary>
public sealed record TurnUsage(int? PromptTokens, int? CompletionTokens);

/// <summary>The last event of a turn that ended normally: the stored message, why it ended, the grounding and the times.</summary>
public sealed record TurnDone(long MessageId, string FinishReason, TurnGrounding Grounding, TurnUsage Usage, int? FirstTokenMs, int TotalMs) : TurnEvent
{
    public override string EventName() => "done";
}

/// <summary>The turn failed: <c>assistant.model_error</c> or <c>assistant.timeout</c>, and a fixed sentence.</summary>
public sealed record TurnError(string Code, string Message) : TurnEvent
{
    public override string EventName() => "error";
}
