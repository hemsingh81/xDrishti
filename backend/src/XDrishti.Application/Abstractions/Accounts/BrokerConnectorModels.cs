// The values exchanged with IBrokerConnector. Secrets, tokens and codes are SecretValue: they print as "[redacted]" in ToString().
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>The Dhan app's API key and secret (app_id / app_secret).</summary>
public sealed record BrokerAppCredentials(SecretValue ApiKey, SecretValue ApiSecret);

/// <summary>What starting a browser sign-in needs: the app and the account's client id.</summary>
public sealed record LoginStartRequest(BrokerAppCredentials App, string ClientId);

/// <summary>The <c>tokenId</c> Dhan sent back to the redirect URL, with the app needed to exchange it.</summary>
public sealed record LoginCompletion(BrokerAppCredentials App, SecretValue Code);

/// <summary>An access token from Dhan with the holder it belongs to and the expiry Dhan stated (null when it states none).</summary>
public sealed record BrokerToken(SecretValue AccessToken, string BrokerUserId, string? BrokerUserName, DateTimeOffset? ExpiresAt);
