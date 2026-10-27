using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace XDrishti.Domain.Assistant;

/// <summary>The cleaned text and the number of instruction-like patterns found in it.</summary>
public sealed record CleanedText(string Text, int InjectionFlags);

/// <summary>
/// Makes untrusted free text safe to hand to the model as DATA (spec P9-02 AC 7, P9-00 D11, D15): control, zero-width and bidirectional characters are removed (line breaks and tabs become a space), <c>&lt;</c> and <c>&gt;</c> are escaped, and instruction-like patterns are COUNTED
/// (<c>ignore previous</c>, <c>system:</c>, a tool name followed by an opening parenthesis); the text is still delivered. Defence is structural (read-only tools, no egress); this keeps the text from looking like markup or a message boundary and makes an attempt visible.
/// </summary>
public static partial class UntrustedText
{
    public static CleanedText Clean(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is '\n' or '\r' or '\t')
            {
                builder.Append(' ');
                continue;
            }

            if (IsHidden(rune))
            {
                continue;
            }

            builder.Append(rune.ToString());
        }

        var stripped = builder.ToString();
        var flags = IgnorePrevious().Count(stripped) + SystemLabel().Count(stripped) + ToolCall().Count(stripped);
        return new CleanedText(stripped.Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal), flags);
    }

    private static bool IsHidden(Rune rune)
    {
        var value = rune.Value;
        if (value is >= 0x200B and <= 0x200F or >= 0x202A and <= 0x202E or >= 0x2060 and <= 0x2064 or >= 0x2066 and <= 0x2069 or 0xFEFF or 0x00AD or 0x061C or 0x180E)
        {
            return true;
        }

        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format;
    }

    [GeneratedRegex(@"ignore\s+(all\s+|any\s+)?(the\s+)?(previous|prior|above)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IgnorePrevious();

    [GeneratedRegex(@"\bsystem\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SystemLabel();

    [GeneratedRegex(@"\b(list_tables|describe_table|run_sql|lab_validate|lab_propose_run|calc|make_chart|search_notes|list_runs|get_[a-z_]+)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ToolCall();
}
