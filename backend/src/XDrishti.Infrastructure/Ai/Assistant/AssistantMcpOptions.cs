using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// Settings <c>Assistant:Mcp</c> (spec P9-03): where the tool server (<c>xd-mcp</c>) listens and the bearer token the API presents. The token comes from a secret file (<c>Assistant__Mcp__Token</c>) and is never part of <see cref="ToString"/>, a log line or an answer. With no address or no token the
/// tool server counts as <c>disabled</c> and nothing is called.
/// </summary>
public sealed class AssistantMcpOptions
{
    public const string SectionName = "Assistant:Mcp";

    public string Url { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    [Range(0, 3600)]
    public int CatalogCacheSeconds { get; set; } = 60;

    [Range(1, 120)]
    public int CallTimeoutSeconds { get; set; } = 15;

    /// <summary>Whether an address and a token are set.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrEmpty(Token);

    /// <summary>The address is empty or an absolute http(s) address.</summary>
    public bool HasValidUrl() => string.IsNullOrWhiteSpace(Url) || (Uri.TryCreate(Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

    public override string ToString() => $"AssistantMcpOptions {{ Configured = {IsConfigured}, Token = {(string.IsNullOrEmpty(Token) ? "(not set)" : "(set)")} }}";
}
