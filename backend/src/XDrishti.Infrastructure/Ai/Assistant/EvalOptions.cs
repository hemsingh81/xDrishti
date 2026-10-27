namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// Settings <c>Eval</c> (spec P9-09). <see cref="Folder"/> is the repo's <c>eval/</c> folder (empty = look for it above the program); <see cref="Mcp"/> is the tool endpoint of the eval (<c>xd-mcp-eval</c>) with its own bearer token (<c>Eval__Mcp__Token</c> from a secret file): it is never
/// the production tool endpoint, and with no address or token the eval tool server counts as disabled and nothing is called. Config only: no endpoint changes them.
/// </summary>
public sealed class EvalOptions
{
    public const string SectionName = "Eval";

    public string Folder { get; set; } = string.Empty;

    public AssistantMcpOptions Mcp { get; set; } = new();
}
