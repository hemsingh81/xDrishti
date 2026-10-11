namespace XDrishti.Api.Auth;

/// <summary>Endpoint marker: no CSRF token needed (only the login, which has no session yet).</summary>
internal sealed class CsrfExemptMetadata
{
    public static readonly CsrfExemptMetadata Instance = new();

    private CsrfExemptMetadata()
    {
    }
}
