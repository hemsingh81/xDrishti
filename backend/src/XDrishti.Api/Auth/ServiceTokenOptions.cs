using Microsoft.Extensions.Options;

namespace XDrishti.Api.Auth;

/// <summary>Settings <c>Api:ServiceTokens</c> (BK-12b): the read-only service tokens. Empty means the scheme accepts nothing. A token is never part of <see cref="ToString"/>, a log line or a response.</summary>
internal sealed class ServiceTokenOptions
{
    public const string SectionName = "Api:ServiceTokens";

    /// <summary>The tool server's token, from the secret file <c>Api__ServiceTokens__Mcp</c>.</summary>
    public string Mcp { get; set; } = string.Empty;

    public override string ToString() => $"ServiceTokenOptions {{ Mcp = {(string.IsNullOrEmpty(Mcp) ? "(not set)" : "(set)")} }}";
}

/// <summary>The service token must differ from every other secret this host holds: one value cannot be revoked alone, and a token that opens the owner's other doors must never double as one (the host refuses to start).</summary>
internal sealed class ServiceTokenOptionsValidator(IConfiguration configuration) : IValidateOptions<ServiceTokenOptions>
{
    /// <summary>The configuration keys of the other credentials of the API.</summary>
    internal static readonly string[] OtherSecrets = ["Assistant:Mcp:Token", "Assistant:Llm:ApiKey", "BrokerVault:Key", "ExecHandover:Key", "Auth:Owner:Password", "Database:Password"];

    public ValidateOptionsResult Validate(string? name, ServiceTokenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.Mcp))
        {
            return ValidateOptionsResult.Success;
        }

        if (options.Mcp.Length < 32)
        {
            return ValidateOptionsResult.Fail("Api:ServiceTokens:Mcp must be at least 32 characters.");
        }

        var shared = OtherSecrets.FirstOrDefault(key => string.Equals(configuration[key], options.Mcp, StringComparison.Ordinal));
        return shared is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail($"Api:ServiceTokens:Mcp equals {shared}; the service token needs a value of its own.");
    }
}
