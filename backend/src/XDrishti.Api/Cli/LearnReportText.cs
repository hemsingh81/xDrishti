using System.Globalization;
using System.Text;

namespace XDrishti.Api.Cli;

/// <summary>Plain-text helpers shared by the CF-12 verbs (<c>xd learn decay|loop|curve</c>): a figure that is missing reads <c>unavailable</c>, never a number, and a long sentence is wrapped to a terminal width.</summary>
internal static class LearnReportText
{
    public const int Width = 100;

    public const string Unavailable = "unavailable";

    /// <summary>Control characters (a stored text is echoed to a terminal) become a space; nothing is cut.</summary>
    public static string Clean(string text) => string.Concat(text.Select(c => char.IsControl(c) ? ' ' : c));

    /// <summary>A figure with a fixed number of decimals, or <c>unavailable</c> when there is none.</summary>
    public static string Figure(double? value, string format = "0.0000") => value is { } v ? v.ToString(format, CultureInfo.InvariantCulture) : Unavailable;

    /// <summary>Every line no wider than <see cref="Width"/>: a longer line is wrapped, its continuation indented two more than its own indent.</summary>
    public static IEnumerable<string> Fit(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.Length <= Width)
            {
                yield return line;
                continue;
            }

            var indent = new string(' ', line.Length - line.TrimStart().Length);
            foreach (var wrapped in Wrap(line.TrimStart(), indent, indent + "  "))
            {
                yield return wrapped;
            }
        }
    }

    /// <summary>The text wrapped to <see cref="Width"/>: the first line starts with <paramref name="first"/>, the rest with <paramref name="rest"/> (default: spaces as wide as <paramref name="first"/>).</summary>
    public static IEnumerable<string> Wrap(string text, string first = "", string? rest = null)
    {
        rest ??= new string(' ', first.Length);
        var line = new StringBuilder(first);
        var lead = first.Length;
        foreach (var word in Clean(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > lead && line.Length + 1 + word.Length > Width)
            {
                yield return line.ToString();
                line.Clear().Append(rest);
                lead = rest.Length;
            }

            if (line.Length > lead)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        yield return line.ToString();
    }
}
