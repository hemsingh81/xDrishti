using System.Threading.RateLimiting;

namespace XDrishti.Api.Auth;

/// <summary>
/// The attempt limit of SWITCHING A CAPABILITY ON (spec P8-18 Part A, ADR 0128): a password is checked there, so the same number of attempts a minute as the sign-in. It is asked in the endpoint after the body is read and only for the ON direction, because the endpoint's route
/// is shared with the OFF direction and the kill switch must never answer 429 (it checks no password). The lockout of the password check still applies on top.
/// </summary>
internal sealed class SwitchOnLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(address =>
        RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions { PermitLimit = Infrastructure.ApiSetup.LoginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    /// <summary>True when this attempt is allowed; otherwise the seconds to wait.</summary>
    public bool TryAcquire(HttpContext http, out int retryAfterSeconds)
    {
        using var lease = _limiter.AttemptAcquire(http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        retryAfterSeconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? (int)Math.Ceiling(wait.TotalSeconds) : 60;
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
