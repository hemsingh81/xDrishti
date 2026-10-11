namespace XDrishti.Application.Auth;

/// <summary>What the client needs to know about its session.</summary>
/// <param name="User">The signed-in owner.</param>
/// <param name="CsrfToken">Must be sent as <c>X-CSRF-Token</c> on every state-changing request.</param>
/// <param name="IdleTimeoutSeconds">The session ends after this long without a request.</param>
/// <param name="ExpiresAt">Absolute end of the session, whatever the activity.</param>
/// <param name="ElevatedUntil">End of the step-up window (password re-entered); null when the session is not elevated right now.</param>
public sealed record SessionInfo(UserInfo User, string CsrfToken, int IdleTimeoutSeconds, DateTimeOffset ExpiresAt, DateTimeOffset? ElevatedUntil = null);
