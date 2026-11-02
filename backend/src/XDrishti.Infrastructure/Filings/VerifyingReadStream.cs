using System.Security.Cryptography;
using XDrishti.Application.Abstractions.Filings;

namespace XDrishti.Infrastructure.Filings;

/// <summary>A forward-only read stream that hashes what it hands out and, at the end of the file, throws <see cref="FilingContentChangedException"/> when the SHA-256 is not the listed one.</summary>
internal sealed class VerifyingReadStream(FileStream inner, string expectedSha256, long expectedSize) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _verified;
    private long _total;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(Cap(buffer));
        return Account(buffer, read);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(Cap(buffer), cancellationToken).ConfigureAwait(false);
        return Account(buffer.Span, read);
    }

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
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>At most the bytes still expected plus one, so that a file that grew is noticed at once and never read past its listed size.</summary>
    private Memory<byte> Cap(Memory<byte> buffer) => buffer[..(int)Math.Min(buffer.Length, expectedSize - _total + 1)];

    private Span<byte> Cap(Span<byte> buffer) => buffer[..(int)Math.Min(buffer.Length, expectedSize - _total + 1)];

    private int Account(Span<byte> buffer, int read)
    {
        if (read > 0)
        {
            _total += read;
            if (_total > expectedSize)
            {
                throw new FilingContentChangedException();
            }

            _hash.AppendData(buffer[..read]);
            return read;
        }

        if (!_verified && buffer.Length > 0)
        {
            _verified = true;
            if (!string.Equals(Convert.ToHexStringLower(_hash.GetHashAndReset()), expectedSha256, StringComparison.Ordinal))
            {
                throw new FilingContentChangedException();
            }
        }

        return 0;
    }
}
