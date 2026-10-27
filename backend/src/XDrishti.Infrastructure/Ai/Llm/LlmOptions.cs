using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Ai.Llm;

/// <summary>
/// Settings <c>Assistant:Llm</c> (spec P9-01). <see cref="Enabled"/> is OFF by default: while it is false the API makes no call to the model server at all. The key comes from a secret file (<c>Assistant__Llm__ApiKey</c>) and is never part of
/// <see cref="ToString"/>, a log line or an answer.
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Assistant:Llm";

    /// <summary>Deployment flag (compose <c>XD_ASSISTANT_LLM</c>); false = the status says "switched off" and nothing is requested.</summary>
    public bool Enabled { get; set; }

    [Required]
    public string BaseUrl { get; set; } = "http://xd-llm:8080/v1";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The alias the server serves (<c>--alias</c>); constant so that changing the model file changes nothing in the client.</summary>
    [Required]
    public string Model { get; set; } = "xd-main";

    [Range(10, 600)]
    public int RequestTimeoutSeconds { get; set; } = 180;

    [Range(1, 10)]
    public int HealthTimeoutSeconds { get; set; } = 3;

    [Range(30, 900)]
    public int ReadyWaitSeconds { get; set; } = 300;

    [Range(0, 60)]
    public int StatusCacheSeconds { get; set; } = 5;

    /// <summary>File name to SHA-256 of the pinned model files (ADR 0005, 0136). A file not listed is reported without a hash.</summary>
    public Dictionary<string, string> KnownModels { get; set; } = new(StringComparer.Ordinal)
    {
        ["Qwen3-30B-A3B-Q4_K_M.gguf"] = "0d003f6662faee786ed5da3e31b29c978de5ae5d275c8794c606a7f3c01aa8f5",
        ["gpt-oss-20b-MXFP4.gguf"] = "27cd6c432c7672cb812a92f611cf3ba7bbc35928262bb1e1253ff4ee6ae35901",
        ["Qwen3-30B-A3B-Instruct-2507-Q4_K_M.gguf"] = "6c997b8af17debdfb01d890214400ccbab00db6acc0ba8da5de1cc906c4774d0",
    };

    /// <summary>The options are written to no log and no answer, but a debugger or a careless interpolation must not show the key either.</summary>
    public override string ToString() => $"LlmOptions {{ Enabled = {Enabled}, Model = {Model}, ApiKey = {(string.IsNullOrEmpty(ApiKey) ? "(not set)" : "(set)")} }}";

    /// <summary>The base address is an absolute http(s) address.</summary>
    public bool HasValidBaseUrl() =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
