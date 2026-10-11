using XDrishti.Domain.Market;

namespace XDrishti.Application.Abstractions.Market;

public enum SourceDownloadKind
{
    /// <summary>The file is available to read.</summary>
    Content,

    /// <summary>The file has not changed since the validators sent.</summary>
    NotModified,

    /// <summary>The file could not be fetched; <see cref="SourceDownload.Failure"/> says why in plain words.</summary>
    Failed,
}

/// <summary>The answer of a source file request. Owns the response: dispose it after reading <see cref="Content"/>.</summary>
public sealed class SourceDownload : IAsyncDisposable
{
    private readonly IAsyncDisposable? _owner;

    private SourceDownload(SourceDownloadKind kind, Stream? content, SourceStamp stamp, string? failure, IAsyncDisposable? owner)
    {
        Kind = kind;
        Content = content;
        Stamp = stamp;
        Failure = failure;
        _owner = owner;
    }

    public SourceDownloadKind Kind { get; }

    /// <summary>The file, streamed (not buffered); limited in size and time by the source.</summary>
    public Stream? Content { get; }

    public SourceStamp Stamp { get; }

    public string? Failure { get; }

    public static SourceDownload WithContent(Stream content, SourceStamp stamp, IAsyncDisposable? owner = null) =>
        new(SourceDownloadKind.Content, content, stamp, null, owner);

    public static SourceDownload NotModified(SourceStamp stamp) => new(SourceDownloadKind.NotModified, null, stamp, null, null);

    public static SourceDownload Failed(string failure) => new(SourceDownloadKind.Failed, null, SourceStamp.None, failure, null);

    public async ValueTask DisposeAsync()
    {
        if (Content is not null)
        {
            await Content.DisposeAsync();
        }

        if (_owner is not null)
        {
            await _owner.DisposeAsync();
        }
    }
}
