using XDrishti.Application.Abstractions.Market;

namespace XDrishti.Infrastructure.Market;

/// <summary>
/// A read-only view of a download that stops it when it grows beyond its size cap or runs past its deadline, with a message safe to show.
/// Counts the bytes after decompression, so a compressed bomb is stopped too.
/// </summary>
internal sealed class GuardedStream(Stream inner, long maxBytes, string what, CancellationToken deadline) : Stream
{
    private long _read;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline);
        int count;
        try
        {
            count = await inner.ReadAsync(buffer, linked.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new SourceReadException($"{what} took too long to download.");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            throw new SourceReadException($"The download of {what} was interrupted.", ex);
        }

        _read += count;
        return _read > maxBytes ? throw new SourceReadException($"{what} is larger than the allowed {maxBytes / (1024 * 1024)} MB.") : count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Read asynchronously.");

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        await base.DisposeAsync();
    }
}
