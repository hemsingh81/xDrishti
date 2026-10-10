using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XDrishti.LlmBench;

internal sealed record RunResult(double TtftMs, double TotalMs, int PromptTokens, int CompletionTokens, double? ServerPromptTps, double? ServerGenTps, string Text, string? Content = null)
{
    /// <summary>The answer without reasoning tokens (what a user would read); <see cref="Text"/> includes the reasoning.</summary>
    public string Answer => Content ?? Text;

    /// <summary>Generation speed: the server's own figure when present, else measured from the stream.</summary>
    public double GenTps => ServerGenTps is > 0 ? ServerGenTps.Value : CompletionTokens <= 1 || TotalMs <= TtftMs ? 0 : (CompletionTokens - 1) / ((TotalMs - TtftMs) / 1000.0);

    public double PromptTps => ServerPromptTps is > 0 ? ServerPromptTps.Value : TtftMs <= 0 ? 0 : PromptTokens / (TtftMs / 1000.0);
}

internal sealed record ChatSettings(string Model, bool DisableThinking, int Seed = 1);

/// <summary>Streams one chat completion from an OpenAI-compatible server and measures time-to-first-token and speed.</summary>
internal sealed class ChatClient(HttpClient http, string baseUrl, ChatSettings settings, TimeProvider clock)
{
    public string Url(string path) => baseUrl.TrimEnd('/') + path;

    public string BuildRequest(string system, string user, int maxTokens, bool stream, JsonNode? tools = null, bool jsonMode = false)
    {
        var messages = new JsonArray();
        if (system.Length > 0)
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = system });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = user });
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = messages,
            ["max_tokens"] = maxTokens,
            ["temperature"] = 0,
            ["seed"] = settings.Seed,
            ["stream"] = stream,
            ["cache_prompt"] = false,
        };
        if (stream)
        {
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        if (settings.DisableThinking)
        {
            body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
        }

        if (tools is not null)
        {
            body["tools"] = tools.DeepClone();
            body["tool_choice"] = "auto";
        }

        if (jsonMode)
        {
            body["response_format"] = new JsonObject { ["type"] = "json_object" };
        }

        return body.ToJsonString();
    }

    public async Task<RunResult> StreamAsync(string system, string user, int maxTokens, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/v1/chat/completions")) { Content = new StringContent(BuildRequest(system, user, maxTokens, stream: true), Encoding.UTF8, "application/json") };
        var started = clock.GetTimestamp();
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Server returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var text = new StringBuilder();
        var content = new StringBuilder();
        double? first = null;
        ServerTimings? timings = null;
        int? promptTokens = null, completionTokens = null;
        var chunks = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (!SseParser.TryParseLine(line, out var chunk, out var done))
            {
                if (done)
                {
                    break;
                }

                continue;
            }

            if (chunk!.HasText)
            {
                first ??= clock.GetElapsedTime(started).TotalMilliseconds;
                chunks++;
                text.Append(chunk.Reasoning).Append(chunk.Content);
                content.Append(chunk.Content);
            }

            timings = chunk.Timings ?? timings;
            promptTokens = chunk.PromptTokens ?? promptTokens;
            completionTokens = chunk.CompletionTokens ?? completionTokens;
        }

        var total = clock.GetElapsedTime(started).TotalMilliseconds;
        return new RunResult(
            first ?? total,
            total,
            timings?.PromptTokens ?? promptTokens ?? 0,
            timings?.PredictedTokens ?? completionTokens ?? chunks,
            timings?.PromptPerSecond,
            timings?.PredictedPerSecond,
            text.ToString(),
            content.ToString());
    }

    /// <summary>Asks the server's tokenizer how many tokens a text is (llama.cpp <c>/tokenize</c>); null when the server has no such endpoint.</summary>
    public async Task<int?> CountTokensAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await PostJsonAsync("/tokenize", new JsonObject { ["content"] = text }.ToJsonString(), cancellationToken);
            return doc.RootElement.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Array ? tokens.GetArrayLength() : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<JsonDocument> PostJsonAsync(string path, string json, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(new Uri(Url(path)), content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return !response.IsSuccessStatusCode ? throw new HttpRequestException($"Server returned {(int)response.StatusCode}: {body}") : JsonDocument.Parse(body);
    }
}
