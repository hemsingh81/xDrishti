namespace XDrishti.Domain.Common;

/// <summary>
/// Why a call to a broker (or a sync) failed, as a fact set where the HTTP status and Dhan's error code are read (ADR 0030). Retry and persistence rules use this kind and never the text of a message, so rewording a message cannot change what is
/// retried. A kind is carried by <see cref="Error.Cause"/>.
/// </summary>
public enum FailureKind
{
    /// <summary>No usable answer (a timeout, a network failure, a 5xx or an unreadable answer): it may pass by itself.</summary>
    Transient,

    /// <summary>Dhan's rate limit (429 or DH-904): it passes by itself.</summary>
    RateLimited,

    /// <summary>Dhan refused the request (another 4xx): trying again with the same input would not help.</summary>
    Rejected,

    /// <summary>The Dhan access token ran out. Only the owner can fix it.</summary>
    TokenExpired,

    /// <summary>Dhan refused the saved access token. Only the owner can fix it.</summary>
    TokenRefused,

    /// <summary>The account is not connected to Dhan. Only the owner can fix it.</summary>
    NotConnected,

    /// <summary>One stretch of dates holds more trades than can be read in one go, even halved.</summary>
    HistoryTooLarge,

    /// <summary>Anything else that went wrong inside the application.</summary>
    Unexpected,

    /// <summary>A save collided with another change of the same data in our own database (a sync or a quote saved the holding meanwhile): nothing was applied, and the next run does the work again. Not a problem with Dhan.</summary>
    DataChanged,
}
