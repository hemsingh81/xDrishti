using System.Globalization;
using System.Text;

namespace XDrishti.SimExport.Csv;

/// <summary>A growable buffer of characters that is reused for every row: appends text and values without allocating once it has grown to the size of the longest row.</summary>
internal sealed class CharBuffer
{
    private const long IndiaOffsetSeconds = (5 * 3600) + (30 * 60);

    private char[] _chars = new char[512];

    private int _length;

    public int Length => _length;

    public ReadOnlySpan<char> Span => _chars.AsSpan(0, _length);

    public void Clear() => _length = 0;

    public void Append(char c)
    {
        Ensure(1);
        _chars[_length++] = c;
    }

    public void Append(ReadOnlySpan<char> text)
    {
        Ensure(text.Length);
        text.CopyTo(_chars.AsSpan(_length));
        _length += text.Length;
    }

    public void Append(Rune rune)
    {
        Ensure(2);
        _length += rune.EncodeToUtf16(_chars.AsSpan(_length));
    }

    public void Append(long value)
    {
        Ensure(20);
        _length += FormatLong(value, _chars.AsSpan(_length));
    }

    public void Append(double value)
    {
        Ensure(NumberText.MaxDoubleChars);
        _length += NumberText.Write(value, _chars.AsSpan(_length));
    }

    public void Append(Guid value)
    {
        Ensure(36);
        value.TryFormat(_chars.AsSpan(_length), out var written, "D");
        _length += written;
    }

    public void Append(DateOnly value)
    {
        Ensure(10);
        value.TryFormat(_chars.AsSpan(_length), out var written, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        _length += written;
    }

    /// <summary>India time (UTC+05:30, no daylight saving) as <c>yyyy-MM-dd HH:mm</c>, derived from the epoch and never from the zone of the machine.</summary>
    public void AppendIndiaTime(long epochSeconds)
    {
        Ensure(16);
        var india = DateTime.UnixEpoch.AddTicks((epochSeconds + IndiaOffsetSeconds) * TimeSpan.TicksPerSecond);
        india.TryFormat(_chars.AsSpan(_length), out var written, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        _length += written;
    }

    private static int FormatLong(long value, Span<char> destination)
    {
        value.TryFormat(destination, out var written, default, CultureInfo.InvariantCulture);
        return written;
    }

    private void Ensure(int extra)
    {
        if (_length + extra > _chars.Length)
        {
            Array.Resize(ref _chars, Math.Max(_chars.Length * 2, _length + extra));
        }
    }
}
