namespace XDrishti.Application.Abstractions.Platform;

/// <summary>
/// The broker allow-list as the code holds it (ADR 0008, 0097), read only: the declared entries and whether a request would be allowed. The phase check compares the entries with a pinned list and asks about requests that would be orders, positions or funds.
/// Nothing here sends a request.
/// </summary>
public interface IBrokerAllowList
{
    /// <summary>The entries, one per allowed endpoint, in a fixed order (method, host and path; the query keys, never a value).</summary>
    IReadOnlyList<string> Entries();

    /// <summary>Whether the guard would let this request through. Nothing is sent.</summary>
    bool Allows(string method, string url);
}
