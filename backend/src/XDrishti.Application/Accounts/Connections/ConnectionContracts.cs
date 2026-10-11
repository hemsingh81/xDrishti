namespace XDrishti.Application.Accounts.Connections;

/// <summary>Where to send the owner's browser to sign in at Dhan (same tab): Dhan's consent login page. The sign-in must come back before ExpiresAt.</summary>
public sealed record ConnectStartView(string AuthorizeUrl, DateTimeOffset ExpiresAt);

/// <summary>The redirect URL to register with the Dhan app, exactly as shown: <c>{PublicBaseUrl}/api/brokers/dhan/callback</c>.</summary>
public sealed record DhanRedirectView(string RedirectUrl);

/// <summary>Where a Dhan sign-in ends: the account (when known) and, when it failed, the error code (see <see cref="ConnectionErrors"/>).</summary>
public sealed record ConnectReturn(Guid? AccountId, string? ErrorCode)
{
    public bool Connected => ErrorCode is null;
}
