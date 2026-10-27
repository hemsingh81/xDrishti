using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Application.Ai;

/// <summary>One prompt of the assistant: its name, version, text and the SHA-256 (hex) of the text, which is what <c>ai.prompts</c> and every message store.</summary>
public sealed record PromptVersion(string Name, string Version, string Text, string Sha256);

/// <summary>
/// The prompts the assistant sends (spec P9-03): a static list of <c>(name, version, sha256)</c>. The chat prompt is the embedded file <c>system.v1.txt</c>; the date and time that are appended at run time are not part of the hash. Later slices (briefing, drafts, proposals, eval) add their own.
/// </summary>
public static class PromptRegistry
{
    private const string ChatResource = "XDrishti.Application.Ai.Prompts.system.v1.txt";
    private const string BriefingResource = "XDrishti.Application.Ai.Prompts.briefing.v1.txt";
    private const string DraftResource = "XDrishti.Application.Ai.Prompts.draft.v1.txt";

    /// <summary>The system prompt of a chat turn.</summary>
    public static PromptVersion Chat { get; } = Load("chat", "v1", ChatResource);

    /// <summary>The system prompt of the daily briefing and the weekly review (spec P9-06).</summary>
    public static PromptVersion Briefing { get; } = Load("briefing", "v1", BriefingResource);

    /// <summary>The system prompt of a strategy draft (spec P9-07); the generated language sheet is added to it at run time and its hash is stored with every draft.</summary>
    public static PromptVersion Draft { get; } = Load("draft", "v1", DraftResource);

    /// <summary>Every registered prompt.</summary>
    public static IReadOnlyList<PromptVersion> All { get; } = [Chat, Briefing, Draft];

    private static PromptVersion Load(string name, string version, string resource)
    {
        using var stream = typeof(PromptRegistry).Assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"The prompt {resource} is not embedded.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = reader.ReadToEnd().ReplaceLineEndings("\n").TrimEnd();
        return new PromptVersion(name, version, text, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }
}
