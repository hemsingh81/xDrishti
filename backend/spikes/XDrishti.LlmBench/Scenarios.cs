using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XDrishti.LlmBench;

internal static class Stats
{
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}

/// <summary>Deterministic synthetic prompts of about N tokens (~4 characters per token); the server reports the real count.</summary>
internal static class PromptFactory
{
    public const double DefaultCharsPerToken = 4;

    public static string Filler(int approxTokens, string seed = "note", double charsPerToken = DefaultCharsPerToken)
    {
        var length = (int)(approxTokens * charsPerToken);
        var sb = new StringBuilder();
        var i = 0;
        while (sb.Length < length)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{seed} {i}: the {(i % 7 == 0 ? "index" : "stock")} traded between {100 + (i % 13)} and {110 + (i % 17)} with volume {1000 + (i * 37 % 9000)} shares. ");
            i++;
        }

        return sb.ToString(0, length);
    }

    public static string Needle(int approxTokens, string secret, double charsPerToken = DefaultCharsPerToken)
    {
        var filler = Filler(approxTokens, "market log", charsPerToken);
        var at = filler.Length * 2 / 3;
        return filler.Insert(at, $" IMPORTANT FACT: the vault code is {secret}. ") + "\n\nWhat is the vault code? Answer with the number only.";
    }

    public static string Briefing(double charsPerToken = DefaultCharsPerToken) => "Today's plan data (JSON-like):\n" + Filler(3850, "ticket", charsPerToken) + "\n\nWrite a daily briefing of about 300 words: regime, the tickets with a one-line rationale each, risks. Use only numbers present in the data.";
}

internal static class ToolSchema
{
    public static JsonNode Tools() => JsonNode.Parse("""
        [{"type":"function","function":{"name":"get_plan","description":"Get the trade plan for a date and account","parameters":{"type":"object","properties":{"date":{"type":"string","description":"ISO date, e.g. 2026-10-09"},"account":{"type":"string","description":"Account name"}},"required":["date","account"]}}}]
        """)!;

    /// <summary>A tool call counts as valid when it names get_plan and its arguments parse as JSON with both required string fields.</summary>
    public static bool IsValidToolCall(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
            var call = message.GetProperty("tool_calls")[0].GetProperty("function");
            if (call.GetProperty("name").GetString() != "get_plan")
            {
                return false;
            }

            var args = call.GetProperty("arguments");
            using var parsed = args.ValueKind == JsonValueKind.String ? JsonDocument.Parse(args.GetString()!) : null;
            var obj = parsed?.RootElement ?? args;
            return obj.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String && obj.TryGetProperty("account", out var a) && a.ValueKind == JsonValueKind.String;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return false;
        }
    }
}

internal sealed record ThroughputResult(int ContextTokens, IReadOnlyList<RunResult> Runs)
{
    public double MedianPromptTps => Stats.Median([.. Runs.Select(r => r.PromptTps)]);

    public double MedianGenTps => Stats.Median([.. Runs.Select(r => r.GenTps)]);

    public double MedianTtftMs => Stats.Median([.. Runs.Select(r => r.TtftMs)]);

    public double MedianTotalMs => Stats.Median([.. Runs.Select(r => r.TotalMs)]);

    public int ActualPromptTokens => Runs.Count == 0 ? 0 : Runs[0].PromptTokens;
}

internal sealed record SmokeResult(string Name, bool Passed, string Detail);

internal sealed record BenchResults(string Label, string Model, DateTimeOffset At, IReadOnlyList<ThroughputResult> Throughput, ThroughputResult? Briefing, IReadOnlyList<SmokeResult> Smoke, double? LoadMs);

internal sealed class Scenarios(ChatClient client, int repeats, int genTokens, Action<string> log)
{
    private double _charsPerToken = PromptFactory.DefaultCharsPerToken;

    /// <summary>Measures characters per token with the server's own tokenizer so "8K context" really means about 8,000 tokens.</summary>
    public async Task CalibrateAsync(CancellationToken cancellationToken)
    {
        var sample = PromptFactory.Filler(1000);
        var tokens = await client.CountTokensAsync(sample, cancellationToken);
        if (tokens is > 0)
        {
            _charsPerToken = (double)sample.Length / tokens.Value;
            log($"Tokenizer calibration: {_charsPerToken:F2} characters per token");
        }
        else
        {
            log("Server has no /tokenize: assuming 4 characters per token (prompt sizes are approximate; actual counts are reported).");
        }
    }

    public async Task<ThroughputResult> ThroughputAsync(int contextTokens, CancellationToken cancellationToken)
    {
        log($"Throughput at ~{contextTokens} tokens context…");
        var prompt = PromptFactory.Filler(contextTokens, "note", _charsPerToken) + "\n\nSummarise the notes above in about 150 words.";
        return await RepeatAsync(contextTokens, prompt, genTokens, cancellationToken);
    }

    public async Task<ThroughputResult> BriefingAsync(CancellationToken cancellationToken)
    {
        log("Briefing-style prompt (~4K tokens in, ~300 words out)…");
        return await RepeatAsync(4000, PromptFactory.Briefing(_charsPerToken), 500, cancellationToken);
    }

    public async Task<IReadOnlyList<SmokeResult>> SmokeAsync(string? embeddingsUrl, CancellationToken cancellationToken)
    {
        var results = new List<SmokeResult>();
        results.Add(await ToolCallsAsync(10, cancellationToken));
        results.Add(await JsonModeAsync(cancellationToken));
        results.Add(await NeedleAsync(cancellationToken));
        if (embeddingsUrl is not null)
        {
            results.Add(await EmbeddingsAsync(embeddingsUrl, cancellationToken));
        }

        return results;
    }

    private async Task<ThroughputResult> RepeatAsync(int contextTokens, string prompt, int maxTokens, CancellationToken cancellationToken)
    {
        await client.StreamAsync(string.Empty, "Say OK.", 4, cancellationToken); // warm-up: model resident, kernels compiled
        var runs = new List<RunResult>();
        for (var i = 0; i < repeats; i++)
        {
            runs.Add(await client.StreamAsync(string.Empty, prompt, maxTokens, cancellationToken));
        }

        return new ThroughputResult(contextTokens, runs);
    }

    private async Task<SmokeResult> ToolCallsAsync(int trials, CancellationToken cancellationToken)
    {
        var valid = 0;
        for (var i = 0; i < trials; i++)
        {
            var request = client.BuildRequest(string.Empty, $"What is the plan for 2026-10-{10 + i:D2} for account main?", 200, stream: false, tools: ToolSchema.Tools());
            try
            {
                using var doc = await client.PostJsonAsync("/v1/chat/completions", request, cancellationToken);
                valid += ToolSchema.IsValidToolCall(doc.RootElement.GetRawText()) ? 1 : 0;
            }
            catch (HttpRequestException)
            {
                // counts as invalid
            }
        }

        return new SmokeResult("tool calls valid", valid >= trials * 0.9, $"{valid}/{trials}");
    }

    private async Task<SmokeResult> JsonModeAsync(CancellationToken cancellationToken)
    {
        var request = client.BuildRequest(string.Empty, "Return a JSON object with keys \"symbol\" (string) and \"side\" (BUY or SELL) for a sample trade.", 512, stream: false, jsonMode: true);
        try
        {
            using var doc = await client.PostJsonAsync("/v1/chat/completions", request, cancellationToken);
            var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            using var parsed = JsonDocument.Parse(text);
            return new SmokeResult("json mode", parsed.RootElement.ValueKind == JsonValueKind.Object, "valid JSON object");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException)
        {
            return new SmokeResult("json mode", false, ex.GetType().Name);
        }
    }

    private async Task<SmokeResult> NeedleAsync(CancellationToken cancellationToken)
    {
        var secret = "4417";
        try
        {
            var run = await client.StreamAsync(string.Empty, PromptFactory.Needle(30000, secret, _charsPerToken), 512, cancellationToken);
            return new SmokeResult("needle in ~30K tokens", run.Answer.Contains(secret, StringComparison.Ordinal), $"prompt tokens {run.PromptTokens}");
        }
        catch (HttpRequestException ex)
        {
            return new SmokeResult("needle in ~30K tokens", false, ex.Message.Length <= 120 ? ex.Message : ex.Message[..120]);
        }
    }

    private static async Task<SmokeResult> EmbeddingsAsync(string embeddingsUrl, CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        var embeddings = new ChatClient(http, embeddingsUrl, new ChatSettings("embed", false), TimeProvider.System);
        try
        {
            using var doc = await embeddings.PostJsonAsync("/v1/embeddings", "{\"input\":[\"search_document: expectancy of 15m setups\"],\"model\":\"embed\"}", cancellationToken);
            var dim = doc.RootElement.GetProperty("data")[0].GetProperty("embedding").GetArrayLength();
            return new SmokeResult("embeddings", dim >= 256, $"{dim} dimensions");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException)
        {
            return new SmokeResult("embeddings", false, ex.GetType().Name);
        }
    }
}
