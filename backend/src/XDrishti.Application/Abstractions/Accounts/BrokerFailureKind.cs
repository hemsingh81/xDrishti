namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>How a broker call failed.</summary>
public enum BrokerFailureKind
{
    /// <summary>The broker answered and refused (a 4xx: wrong key or secret, expired or used code).</summary>
    Rejected,

    /// <summary>The broker refused the saved access token (401 or 403): it expired or was revoked. Only reported by calls that send a token.</summary>
    Unauthorized,

    /// <summary>No usable answer: timeout, network failure, 5xx or a response that could not be read.</summary>
    Unavailable,
}
