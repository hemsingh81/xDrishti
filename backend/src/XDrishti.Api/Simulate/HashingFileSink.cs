using System.Buffers;
using System.Security.Cryptography;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The sink the export library renders into (spec P3-04b1, E7): an <see cref="IBufferWriter{T}"/> over a stream that keeps one bounded buffer, writes it to the stream when it fills and hashes what it writes (SHA-256) while counting the
/// bytes, so the hash of a file of tens of megabytes is known without reading it back and memory stays flat. <see cref="Complete"/> flushes the last buffer and gives the length and the lower-case hex hash.
/// </summary>
internal sealed class HashingFileSink : IBufferWriter<byte>, IDisposable
{
    public const int DefaultBufferSize = 64 * 1024;

    private readonly Stream _stream;

    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    private byte[] _buffer;

    private int _position;

    private long _bytes;

    private bool _completed;

    public HashingFileSink(Stream stream, int bufferSize = DefaultBufferSize)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferSize, 1);

        _stream = stream;
        _buffer = new byte[bufferSize];
    }

    public void Advance(int count)
    {
        ThrowIfCompleted();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > _buffer.Length - _position)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "More bytes than the span that was handed out.");
        }

        _position += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Reserve(sizeHint);
        return _buffer.AsMemory(_position);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Reserve(sizeHint);
        return _buffer.AsSpan(_position);
    }

    /// <summary>Writes the last buffer to the stream and returns the number of bytes written and their SHA-256 in lower-case hex. The sink takes nothing after it.</summary>
    public (long Bytes, string Sha256) Complete()
    {
        ThrowIfCompleted();
        Flush();
        _completed = true;
        return (_bytes, Convert.ToHexStringLower(_hash.GetHashAndReset()));
    }

    public void Dispose() => _hash.Dispose();

    private void Reserve(int sizeHint)
    {
        ThrowIfCompleted();
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        var needed = Math.Max(sizeHint, 1);
        if (_buffer.Length - _position >= needed)
        {
            return;
        }

        Flush();
        if (_buffer.Length < needed)
        {
            _buffer = new byte[needed];
        }
    }

    private void Flush()
    {
        if (_position == 0)
        {
            return;
        }

        _stream.Write(_buffer, 0, _position);
        _hash.AppendData(_buffer, 0, _position);
        _bytes += _position;
        _position = 0;
    }

    private void ThrowIfCompleted() => ObjectDisposedException.ThrowIf(_completed, this);
}
