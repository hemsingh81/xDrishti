using System.ComponentModel.DataAnnotations;

namespace XDrishti.Mcp;

/// <summary>
/// Settings <c>Mcp</c> (spec P9-02). The limits of the tool contract (rows 200, 6,000 characters, 64 KB, 4,000 SQL characters) are constants in <c>ToolLimits</c>, not options. <see cref="Tokens"/> is one bearer token per client (<c>Api</c>; <c>Hermes</c> is added by P9-10), each from its own
/// secret file (<c>Mcp__Tokens__Api</c>); a client with no token cannot call. A token is never part of <see cref="ToString"/>, a log line or an answer.
/// </summary>
public sealed class McpOptions : IValidatableObject
{
    public const string SectionName = "Mcp";

    [Range(1, 60)]
    public int ToolTimeoutSeconds { get; set; } = 10;

    [Range(1, 30)]
    public int SqlTimeoutSeconds { get; set; } = 5;

    [Range(1, 6000)]
    public int RateLimitPerMinute { get; set; } = 60;

    /// <summary>Client name to bearer token.</summary>
    public Dictionary<string, string> Tokens { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Two clients must never share a token value: the gate would name the first match, and one token could not be revoked alone (P9-10 review). Empty tokens never match, so they are not compared.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var shared = Tokens.Where(t => !string.IsNullOrEmpty(t.Value)).GroupBy(t => t.Value, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => string.Join(", ", g.Select(t => t.Key).Order(StringComparer.Ordinal))).ToList();
        foreach (var clients in shared)
        {
            yield return new ValidationResult($"Clients {clients} share one token; every client needs its own.", [nameof(Tokens)]);
        }
    }

    public override string ToString() => $"McpOptions {{ ToolTimeoutSeconds = {ToolTimeoutSeconds}, SqlTimeoutSeconds = {SqlTimeoutSeconds}, RateLimitPerMinute = {RateLimitPerMinute}, Tokens = {Tokens.Count} (hidden) }}";
}
