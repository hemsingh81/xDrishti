namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>How a broker call failed.</summary>
public enum BrokerFailureKind
{
    /// <summary>The broker answered and refused (a 4xx: wrong key or secret, expired or used code).</summary>
    Rejected,

    /// <summary>No usable answer: timeout, network failure, 5xx or a response that could not be read.</summary>
    Unavailable,
}
