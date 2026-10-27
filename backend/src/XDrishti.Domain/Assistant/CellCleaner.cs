using System.Collections;

namespace XDrishti.Domain.Assistant;

/// <summary>Counts what the cleaning of a result changed (the <c>redactions</c> and <c>injectionFlags</c> of the envelope).</summary>
public sealed class CleanCounters
{
    public int Redactions { get; private set; }

    public int InjectionFlags { get; private set; }

    internal void Add(int redactions, int flags)
    {
        Redactions += redactions;
        InjectionFlags += flags;
    }
}

/// <summary>
/// The one place where a result cell is made ready for the model (spec P9-02 AC 6 and 7). A string in a text column is cleaned as untrusted text, scrubbed, and cut to <see cref="ToolLimits.MaxCellChars"/>; a string in any other column is only cut; numbers, flags and other values are
/// untouched. Arrays are handled element by element.
/// </summary>
public static class CellCleaner
{
    public static object? Clean(object? value, bool isText, CleanCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        switch (value)
        {
            case string text when isText:
                var cleaned = UntrustedText.Clean(text);
                var scrubbed = OutputScrubber.Scrub(cleaned.Text, out var redactions);
                counters.Add(redactions, cleaned.InjectionFlags);
                return ToolLimits.CutCell(scrubbed);
            case string text:
                return ToolLimits.CutCell(text);
            case IEnumerable items and not string:
                var list = new List<object?>();
                foreach (var item in items)
                {
                    list.Add(Clean(item, isText, counters));
                }

                return list;
            default:
                return value;
        }
    }
}
