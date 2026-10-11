using System.Text;

namespace XDrishti.Application.Market.Import;

/// <summary>
/// Reads CSV one record at a time (RFC 4180 quoting, embedded line breaks) from a stream, without loading the file. A record longer than
/// <see cref="MaxRecordLength"/> characters is refused (<see cref="FormatException"/>) before it is held in memory: it is not a table row.
/// </summary>
internal sealed class CsvRecordReader(TextReader reader)
{
    public const int MaxRecordLength = 64 * 1024;

    private readonly StringBuilder _field = new();

    private readonly StringBuilder _line = new();

    private readonly char[] _buffer = new char[16 * 1024];

    private int _position;

    private int _length;

    public long RecordNumber { get; private set; }

    /// <summary>The next physical line (<c>\n</c>, <c>\r\n</c> or <c>\r</c> ends it), not split; null at the end. Cheap pre-filtering of rows that need no parsing.</summary>
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        _line.Clear();
        var any = false;
        while (true)
        {
            if (_position >= _length)
            {
                _length = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken);
                _position = 0;
                if (_length == 0)
                {
                    if (!any)
                    {
                        return null;
                    }

                    break;
                }
            }

            var chunk = _buffer.AsSpan(_position, _length - _position);
            var end = chunk.IndexOfAny('\n', '\r');
            if (end < 0)
            {
                Append(chunk);
                _position = _length;
                any = true;
                continue;
            }

            Append(chunk[..end]);
            any = true;
            var terminator = chunk[end];
            _position += end + 1;
            if (terminator == '\r')
            {
                if (_position >= _length)
                {
                    _length = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken);
                    _position = 0;
                }

                if (_length > 0 && _buffer[_position] == '\n')
                {
                    _position++;
                }
            }

            break;
        }

        RecordNumber++;
        return _line.ToString();
    }

    private void Append(ReadOnlySpan<char> text)
    {
        if (_line.Length + text.Length > MaxRecordLength)
        {
            throw new FormatException($"A line is longer than {MaxRecordLength} characters.");
        }

        _line.Append(text);
    }

    /// <summary>Splits a line into fields; a quoted field that continues on the next line is completed from the reader.</summary>
    public async ValueTask<string[]> SplitAsync(string line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);
        var fields = new List<string>(40);
        _field.Clear();
        var inQuotes = false;
        var i = 0;
        while (true)
        {
            if (i >= line.Length)
            {
                if (!inQuotes)
                {
                    break;
                }

                // A quoted field runs on into the next line.
                line = await ReadLineAsync(cancellationToken) ?? throw new FormatException("The file ends inside a quoted field.");
                _field.Append('\n');
                i = 0;
                continue;
            }

            if (_field.Length > MaxRecordLength)
            {
                throw new FormatException($"A field is longer than {MaxRecordLength} characters.");
            }

            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        _field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    _field.Append(c);
                }
            }
            else if (c == '"' && _field.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(_field.ToString());
                _field.Clear();
            }
            else
            {
                _field.Append(c);
            }

            i++;
        }

        fields.Add(_field.ToString());
        return [.. fields];
    }
}
