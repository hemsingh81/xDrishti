using System.Globalization;

namespace XDrishti.SimExport;

/// <summary>
/// The number format of the export without an allocation (spec P3-04b1, E2): the same text as <see cref="XDrishti.Domain.Research.CanonicalText.Number(double)"/> (the round-trip form, invariant culture, <c>-0</c> as <c>0</c>), written
/// into a span. A NaN or an infinity throws, where <c>CanonicalText</c> would write <c>NaN</c>: a file must never hold a value that is not a number. A test compares the two over a corpus.
/// </summary>
internal static class NumberText
{
    /// <summary>The longest text of a double in the round-trip form (for example <c>-1.7976931348623157E+308</c>) fits in this many characters.</summary>
    public const int MaxDoubleChars = 32;

    /// <summary>Writes the number and returns how many characters it took.</summary>
    /// <exception cref="ArgumentException">The value is NaN or an infinity.</exception>
    public static int Write(double value, Span<char> destination)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException("A NaN or an infinity cannot be written to an export file.", nameof(value));
        }

        if (value == 0)
        {
            destination[0] = '0';
            return 1;
        }

        if (!value.TryFormat(destination, out var written, "R", CultureInfo.InvariantCulture))
        {
            throw new ArgumentException("The destination is too short for the number.", nameof(destination));
        }

        return written;
    }
}
