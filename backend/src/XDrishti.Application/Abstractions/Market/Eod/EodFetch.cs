namespace XDrishti.Application.Abstractions.Market.Eod;

/// <summary>How a fetch of one file ended. <c>Absent</c> is "NSE has no such file" (a holiday, or a date before the archive); <c>NotAllowed</c> is the allow-list refusing before anything was sent.</summary>
public enum EodFetchStatus
{
    Ok,
    Absent,
    NotAllowed,
    Failed,
}

/// <summary>The answer of <see cref="IEodSource"/>: the file's bytes (already unzipped) when <see cref="Status"/> is <see cref="EodFetchStatus.Ok"/>, otherwise a short safe message (host and path only, never a URL with a query or any content).</summary>
public sealed record EodFetch(EodFetchStatus Status, byte[]? Content = null, string? Message = null)
{
    public static EodFetch Ok(byte[] content) => new(EodFetchStatus.Ok, content);

    public static EodFetch Absent() => new(EodFetchStatus.Absent);

    public static EodFetch NotAllowed(string message) => new(EodFetchStatus.NotAllowed, null, message);

    public static EodFetch Failed(string message) => new(EodFetchStatus.Failed, null, message);
}
