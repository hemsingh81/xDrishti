namespace XDrishti.Application.Abstractions.Market.Lists;

/// <summary>How one archive request ended.</summary>
public enum ListFetchStatus
{
    /// <summary>The file came back.</summary>
    Ok,

    /// <summary>NSE has no such file (HTTP 404: a holiday or a date outside the archive).</summary>
    Absent,

    /// <summary>The request failed or the answer was unusable; nothing is stored.</summary>
    Failed,

    /// <summary>The allow-list does not hold the endpoint: refused before anything was sent.</summary>
    NotAllowed,
}

/// <summary>The outcome of one archive request: the status, the file bytes when <see cref="ListFetchStatus.Ok"/>, a short message otherwise.</summary>
public sealed record ListArchiveFetch(ListFetchStatus Status, byte[]? Content = null, string? Message = null)
{
    public static ListArchiveFetch Ok(byte[] content) => new(ListFetchStatus.Ok, content);

    public static ListArchiveFetch Absent() => new(ListFetchStatus.Absent);

    public static ListArchiveFetch Failed(string message) => new(ListFetchStatus.Failed, null, message);

    public static ListArchiveFetch NotAllowed(string message) => new(ListFetchStatus.NotAllowed, null, message);
}
