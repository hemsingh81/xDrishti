using System.Buffers;
using System.Text;

namespace XDrishti.SimExport.Tests.Support;

/// <summary>The sinks and renderers the tests use: a growing buffer to read the output as text, and a sink that keeps nothing but the count (for the size and memory tests).</summary>
internal static class Sinks
{
    public static string Render(Action<IBufferWriter<byte>> write)
    {
        var sink = new ArrayBufferWriter<byte>();
        write(sink);
        return new UTF8Encoding(false, true).GetString(sink.WrittenSpan);
    }

    public static byte[] Bytes(Action<IBufferWriter<byte>> write)
    {
        var sink = new ArrayBufferWriter<byte>();
        write(sink);
        return sink.WrittenSpan.ToArray();
    }
}

/// <summary>An <see cref="IBufferWriter{T}"/> that keeps one small buffer and counts what was written: the output is never held, as with the file sink of the host.</summary>
internal sealed class CountingSink : IBufferWriter<byte>
{
    private byte[] _buffer = new byte[64 * 1024];

    public long Bytes { get; private set; }

    public void Advance(int count) => Bytes += count;

    public Memory<byte> GetMemory(int sizeHint = 0) => Buffer(sizeHint);

    public Span<byte> GetSpan(int sizeHint = 0) => Buffer(sizeHint).Span;

    private Memory<byte> Buffer(int sizeHint)
    {
        if (sizeHint > _buffer.Length)
        {
            _buffer = new byte[sizeHint];
        }

        return _buffer;
    }
}
