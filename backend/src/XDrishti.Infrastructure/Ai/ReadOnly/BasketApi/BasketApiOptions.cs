using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Ai.ReadOnly.BasketApi;

/// <summary>
/// Settings <c>Mcp:Api</c> (BK-12b, ADR 0267): where the tool server reads the two basket reports (the API of the same stack) and the read-only service token it presents (secret file <c>Mcp__Api__Token</c>). The token is never part of <see cref="ToString"/>, a log line, an exception or an answer.
/// With no address or no token every basket report answers <c>tool.unavailable</c> and nothing is called.
/// </summary>
internal sealed class BasketApiOptions
{
    public const string SectionName = "Mcp:Api";

    public string BaseUrl { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    /// <summary>Inside the tool timeout (10 s) so that a slow API is an answer, not a timeout of the tool.</summary>
    [Range(1, 9)]
    public int TimeoutSeconds { get; set; } = 8;

    [Range(1024, 16 * 1024 * 1024)]
    public int MaxResponseBytes { get; set; } = 2 * 1024 * 1024;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrEmpty(Token);

    /// <summary>The address is empty or an absolute http(s) address.</summary>
    public bool HasValidUrl() => string.IsNullOrWhiteSpace(BaseUrl) || (Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

    public override string ToString() => $"BasketApiOptions {{ Configured = {IsConfigured}, Token = {(string.IsNullOrEmpty(Token) ? "(not set)" : "(set)")} }}";
}
