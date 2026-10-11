using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Accounts;

/// <summary>Broker settings that depend on the deployment (configuration section <c>Brokers</c>).</summary>
internal sealed class BrokersOptions
{
    public const string SectionName = "Brokers";

    public const string InvalidBaseUrlMessage = "Brokers:PublicBaseUrl must be an absolute http(s) URL without path, query or fragment, e.g. http://localhost:8080.";

    /// <summary>Where the owner's browser reaches xDrishti; the brokers redirect back to it after a login.</summary>
    [Required]
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";

    public static bool IsValidBaseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.AbsolutePath == "/"
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0
        && uri.UserInfo.Length == 0;
}
