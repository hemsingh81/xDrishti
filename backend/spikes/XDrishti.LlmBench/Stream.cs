using System.Globalization;
using System.Text.Json;

namespace XDrishti.LlmBench;

/// <summary>llama.cpp's own timing block (sent with the final streamed chunk).</summary>
internal sealed record ServerTimings(int PromptTokens, double PromptMs, double PromptPerSecond, int PredictedTokens, double PredictedMs, double PredictedPerSecond);

internal sealed record StreamChunk(string? Content, string? Reasoning, string? FinishReason, ServerTimings? Timings, int? PromptTokens, int? CompletionTokens)
{
    public bool HasText => !string.IsNullOrEmpty(Content) || !string.IsNullOrEmpty(Reasoning);
}

/// <summary>Parses OpenAI-style server-sent events (<c>data: {json}</c> lines, terminated by <c>data: [DONE]</c>).</summary>
internal static class SseParser
{
    /// <summary>Returns false for blank lines, comments and the [DONE] marker (<paramref name="done"/> tells the last one apart).</summary>
    public static bool TryParseLine(string line, out StreamChunk? chunk, out bool done)
    {
        chunk = null;
        done = false;
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith(':'))
        {
            return false;
        }

        if (!line.StartsWith("data:", StringComparison.Ordinal))
        {
            throw new FormatException($"Unexpected stream line: {Truncate(line)}");
        }

        var payload = line["data:".Length..].Trim();
        if (payload == "[DONE]")
        {
            done = true;
            return false;
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
        {
            throw new HttpRequestException($"Server reported an error in the stream: {Truncate(error.ToString())}");
        }

        string? content = null, reasoning = null, finish = null;
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
            {
                content = String(delta, "content");
                reasoning = String(delta, "reasoning_content") ?? String(delta, "reasoning");
            }

            finish = String(choice, "finish_reason");
        }

        int? promptTokens = null, completionTokens = null;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            promptTokens = Int(usage, "prompt_tokens");
            completionTokens = Int(usage, "completion_tokens");
        }

        chunk = new StreamChunk(content, reasoning, finish, root.TryGetProperty("timings", out var t) ? Timings(t) : null, promptTokens, completionTokens);
        return true;
    }

    private static ServerTimings Timings(JsonElement t) => new(
        Int(t, "prompt_n") ?? 0,
        Double(t, "prompt_ms"),
        Double(t, "prompt_per_second"),
        Int(t, "predicted_n") ?? 0,
        Double(t, "predicted_ms"),
        Double(t, "predicted_per_second"));

    private static string? String(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static double Double(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static string Truncate(string text) => text.Length <= 80 ? text : text[..80].ToString(CultureInfo.InvariantCulture);
}
