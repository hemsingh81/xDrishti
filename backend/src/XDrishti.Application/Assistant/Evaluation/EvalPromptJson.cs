using System.Text.Json;
using XDrishti.Domain.Assistant.Evaluation;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>The prompt list as stored with a run (<c>prompt_versions_json</c>): a JSON array of name, version and hash.</summary>
internal static class EvalPromptJson
{
    public static string Write(IReadOnlyList<PromptStamp> prompts) => JsonSerializer.Serialize(prompts);

    public static IReadOnlyList<PromptStamp> Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<PromptStamp>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
