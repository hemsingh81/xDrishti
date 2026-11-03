using System.Globalization;
using System.Text;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.PairResearch;

/// <summary>The content address of a formation: SHA-256 of the family, quarter, window, the pre-registered options and every member's closes. The same closes give the same id; nothing dated on or after the quarter is part of it.</summary>
public static class PairFormationId
{
    public static string Of(string quarterLabel, DateOnly from, DateOnly to, PairStudyOptions options, IReadOnlyList<PairMember> members)
    {
        ArgumentNullException.ThrowIfNull(quarterLabel);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(members);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"pair-formation/1\n{PairFamilies.Intraday}\n{quarterLabel}\n{from:yyyy-MM-dd}\n{to:yyyy-MM-dd}\n{options.Canonical()}\n");
        foreach (var m in members)
        {
            text.Append(m.Symbol).Append('|').Append(m.Sector).Append('|').Append(m.MeanTurnover.ToString("R", CultureInfo.InvariantCulture));
            foreach (var close in m.Closes)
            {
                text.Append(',').Append(close.ToString("R", CultureInfo.InvariantCulture));
            }

            text.Append('\n');
        }

        return ExtensionFamilies.Sha256Hex(text.ToString());
    }
}
