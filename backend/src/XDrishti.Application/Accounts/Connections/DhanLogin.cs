namespace XDrishti.Application.Accounts.Connections;

/// <summary>Where Dhan sends the owner's browser after the consent login.</summary>
internal static class DhanLogin
{
    /// <summary>The path Dhan redirects to; appended to the public base URL. Registered with the Dhan app as the redirect URL.</summary>
    public const string CallbackPath = "/api/brokers/dhan/callback";

    public static string RedirectUrl(string publicBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(publicBaseUrl);
        return publicBaseUrl.TrimEnd('/') + CallbackPath;
    }
}
