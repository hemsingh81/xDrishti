using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Application.Ai;

/// <summary>
/// The size rules of a prompt (spec P9-03 AC 5, P9-00 token budget): a token is estimated as characters / 3.5; the tool schemas may take 2,200 tokens, the history 1,500; the history is the last messages, and an assistant message carries a one-line summary of its tool calls, never their results.
/// Pure; no I/O.
/// </summary>
public static class ContextBudget
{
    public const double CharactersPerToken = 3.5;
    public const int SchemaTokenLimit = 2200;
    public const int HistoryTokenLimit = 1500;
    public const int SummaryArgumentChars = 120;

    /// <summary>The estimated tokens of a text.</summary>
    public static int EstimateTokens(string? text) => string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / CharactersPerToken);

    /// <summary>The estimated tokens the tool declarations take (name, description and schema of each).</summary>
    public static int SchemaTokens(IEnumerable<ToolDescriptor> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return tools.Sum(t => EstimateTokens(t.Name) + EstimateTokens(t.Description) + EstimateTokens(t.InputSchemaJson));
    }

    /// <summary>
    /// The stored messages as the model sees them, oldest first, at most <paramref name="tokenLimit"/> estimated tokens (the oldest are dropped first; the newest message is dropped last). An assistant message is followed by one line that names the calls of its turn
    /// (number, tool, the arguments cut to 120 characters, how it ended and the row count); the results are not repeated, so a figure of an old turn is not "this turn's tool result".
    /// </summary>
    public static IReadOnlyList<ModelMessage> History(IReadOnlyList<HistoryMessage> stored, int tokenLimit = HistoryTokenLimit)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var messages = stored.Select(Render).ToList();
        var total = messages.Sum(m => EstimateTokens(m.Content));
        while (messages.Count > 0 && total > tokenLimit)
        {
            total -= EstimateTokens(messages[0].Content);
            messages.RemoveAt(0);
        }

        return messages;
    }

    private static ModelMessage Render(HistoryMessage message)
    {
        var assistant = string.Equals(message.Role, "assistant", StringComparison.Ordinal);
        var text = message.Content;
        if (assistant && message.ToolCalls.Count > 0)
        {
            var calls = string.Join("; ", message.ToolCalls.Select(c => $"#{c.CallIndex} {c.Tool} {Cut(c.ArgumentsJson)} -> {c.Status}, {c.Rows} rows"));
            text = $"{text}\n(Tools used in that turn, results not repeated: {calls})";
        }

        return new ModelMessage(assistant ? ModelRole.Assistant : ModelRole.User, text);
    }

    private static string Cut(string text) => text.Length <= SummaryArgumentChars ? text : text[..SummaryArgumentChars] + "...";
}
