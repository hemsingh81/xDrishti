using System.Buffers;
using System.Globalization;
using System.Text;

namespace XDrishti.SimExport.Csv;

/// <summary>
/// One row of a CSV file under construction (spec P3-04b1, E3): the cells are written in order, separated by commas, and the row is encoded as UTF-8 into the sink with a line feed at the end. Numbers go through
/// <see cref="NumberText"/>, never as free text, so that a negative number is not mistaken for a formula; text cells go through <see cref="Text"/>, which removes control and format characters, defuses a formula and quotes as RFC 4180
/// says. The buffers are reused for every row.
/// </summary>
internal sealed class CsvRow
{
    private readonly CharBuffer _row = new();

    private bool _first = true;

    /// <summary>A second buffer for composing a text cell from parts (for example the key of a trade) before it goes through <see cref="Text"/>.</summary>
    public CharBuffer Scratch { get; } = new();

    /// <summary>Starts a new row.</summary>
    public void Begin()
    {
        _row.Clear();
        _first = true;
    }

    /// <summary>A cell that is known to be safe text of the file format (a column name, a spelling, hexadecimal): written as it is.</summary>
    public void Raw(ReadOnlySpan<char> text)
    {
        Separate();
        _row.Append(text);
    }

    public void Number(double value)
    {
        Separate();
        _row.Append(value);
    }

    public void Number(long value)
    {
        Separate();
        _row.Append(value);
    }

    public void Bool(bool value) => Raw(value ? "true" : "false");

    public void Date(DateOnly value)
    {
        Separate();
        _row.Append(value);
    }

    public void IndiaTime(long epochSeconds)
    {
        Separate();
        _row.AppendIndiaTime(epochSeconds);
    }

    public void Empty() => Separate();

    /// <summary>
    /// A text cell, in this order: every character of the Unicode category Control (a NUL, tab, CR or LF inside a cell) or Format (the bidi controls, zero-width characters) is dropped; then, when the first character of the original text or
    /// of the stripped text is <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, TAB, CR or LF, the cell gains a leading apostrophe (the two tests together close the bypasses a single test leaves); a cell with a comma or a quote is quoted and its
    /// quotes doubled.
    /// </summary>
    public void Text(ReadOnlySpan<char> text)
    {
        Separate();
        var prefix = !text.IsEmpty && IsFormulaStart(text[0]);
        var quote = false;
        var firstKept = true;
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsDropped(rune))
            {
                continue;
            }

            if (firstKept)
            {
                firstKept = false;
                prefix |= rune.IsAscii && IsFormulaStart((char)rune.Value);
            }

            quote |= rune.Value is ',' or '"';
        }

        if (quote)
        {
            _row.Append('"');
        }

        if (prefix)
        {
            _row.Append('\'');
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsDropped(rune))
            {
                continue;
            }

            if (rune.Value == '"')
            {
                _row.Append('"');
            }

            _row.Append(rune);
        }

        if (quote)
        {
            _row.Append('"');
        }
    }

    /// <summary>Ends the row with a line feed and writes it to the sink as UTF-8.</summary>
    public void End(IBufferWriter<byte> sink)
    {
        _row.Append('\n');
        var chars = _row.Span;
        var span = sink.GetSpan(Encoding.UTF8.GetMaxByteCount(chars.Length));
        sink.Advance(Encoding.UTF8.GetBytes(chars, span));
    }

    private static bool IsFormulaStart(char c) => c is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n';

    private static bool IsDropped(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.Control or UnicodeCategory.Format;
    }

    private void Separate()
    {
        if (!_first)
        {
            _row.Append(',');
        }

        _first = false;
    }
}
