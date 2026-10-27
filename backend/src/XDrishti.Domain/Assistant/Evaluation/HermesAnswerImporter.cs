using System.Globalization;
using System.Text.Json;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>The answers of the Hermes PoC with the tool results taken from its own session store, ready for <c>xd eval grade</c>, and the per-tool call counts for the cross-check against the <c>xd-mcp</c> log.</summary>
public sealed record ImportedAnswers(string Jsonl, IReadOnlyDictionary<string, int> CallsPerTool, int AnswersWithoutSession, bool? CountsMatchMcpLog);

/// <summary>
/// Merges <c>answers.jsonl</c> (the P9-09 format plus <c>session_ref</c>) with an export of Hermes' session store into answer lines that carry <c>tool_results</c> (spec P9-10 AC 9). Pure.
/// The export format is UNVERIFIED (spec: found in check H-8); the assumed shape is <c>{"sessions":[{"id","tool_calls":[{"name","arguments","result"}]}]}</c>, with tool names optionally prefixed <c>mcp_xd_</c>. An answer whose session is
/// missing keeps no <c>tool_results</c>, so grounding is not evaluable and the answer cannot pass. A malformed export is refused, never guessed at.
/// </summary>
public static class HermesAnswerImporter
{
    public const string InvalidCode = "eval.invalid_hermes_export";
    private const string ToolPrefix = "mcp_xd_";

    public static Result<ImportedAnswers> Import(string answersJsonl, string? sessionExportJson, IReadOnlyDictionary<string, int>? mcpCallsLogged)
    {
        ArgumentNullException.ThrowIfNull(answersJsonl);
        var sessions = new Dictionary<string, List<(string Tool, string Result)>>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(sessionExportJson))
        {
            try
            {
                using var export = JsonDocument.Parse(sessionExportJson);
                foreach (var session in export.RootElement.GetProperty("sessions").EnumerateArray())
                {
                    var calls = new List<(string, string)>();
                    foreach (var call in session.GetProperty("tool_calls").EnumerateArray())
                    {
                        var name = call.GetProperty("name").GetString() ?? string.Empty;
                        var result = call.TryGetProperty("result", out var r) ? (r.ValueKind == JsonValueKind.String ? r.GetString() ?? string.Empty : r.GetRawText()) : string.Empty;
                        calls.Add((name.StartsWith(ToolPrefix, StringComparison.Ordinal) ? name[ToolPrefix.Length..] : name, result));
                    }

                    sessions[session.GetProperty("id").GetString() ?? string.Empty] = calls;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                return Result.Failure<ImportedAnswers>(new Error(InvalidCode, "The session export is not in the expected shape."));
            }
        }

        var output = new List<string>();
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var without = 0;
        var number = 0;
        foreach (var line in answersJsonl.Split('\n'))
        {
            number++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line)!;
                _ = fields["answer"].GetString() ?? throw new FormatException();
                var reference = fields.TryGetValue("session_ref", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                var copy = new Dictionary<string, object?>();
                foreach (var (key, value) in fields.Where(f => f.Key is not ("session_ref" or "tool_results")))
                {
                    copy[key] = value;
                }

                if (reference is not null && sessions.TryGetValue(reference, out var calls) && calls.Count > 0)
                {
                    copy["tool_results"] = calls.Select(c => c.Result).ToList();
                    foreach (var call in calls)
                    {
                        counts[call.Tool] = counts.GetValueOrDefault(call.Tool) + 1;
                    }
                }
                else
                {
                    without++;
                }

                output.Add(JsonSerializer.Serialize(copy));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or NullReferenceException)
            {
                return Result.Failure<ImportedAnswers>(new Error(InvalidCode, string.Create(CultureInfo.InvariantCulture, $"answers line {number} is not an answer.")));
            }
        }

        bool? match = mcpCallsLogged is null ? null : counts.Count == mcpCallsLogged.Count && counts.All(c => mcpCallsLogged.TryGetValue(c.Key, out var logged) && logged == c.Value);
        return Result.Success(new ImportedAnswers(string.Join('\n', output), counts, without, match));
    }
}
