using System.Globalization;
using System.Text.RegularExpressions;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;

namespace XDrishti.Application.Shadow;

/// <summary>What the shadow row reads from a stored plan item, and only from it (spec CF-08, review fix 3): never a recomputation.</summary>
internal static partial class ShadowPlanFacts
{
    /// <summary>
    /// Whether the item counts as edge evidence: a primary or reserve ticket, or a candidate rejected by the fill, which all went through the smart rules and the fill. An item the plan marks as never having reached them (held, not eligible, or dropped in the merge as a
    /// duplicate or a conflict) is recorded but set aside (decision D1).
    /// </summary>
    public static bool CountsForEdge(NewPlanItem item) => !item.Evidence.Unavailable.Contains(SelectionPlanBuilder.SmartRulesNotRun, StringComparer.Ordinal);

    /// <summary>
    /// The model's advisory probability, read from the item's stored advisory note (<c>model P(win) 0.712, advisory only</c>, three decimals as the builder wrote it), or null when the plan holds none. The plan record has no structured model figure or model id
    /// (UNVERIFIED in the spec, resolved: it does not), so the id stays null.
    /// </summary>
    public static double? AdvisoryPWin(NewPlanItem item)
    {
        var note = item.Evidence.AdvisoryNote;
        if (note is null || NoteShape().Match(note) is not { Success: true } match)
        {
            return null;
        }

        return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p is >= 0 and <= 1 ? p : null;
    }

    /// <summary>The group in plan order for the unique trade key: primary first, then reserve, then rejected, each by rank, so the best-placed candidate keeps a key two candidates share.</summary>
    public static IEnumerable<PlanItemRow> InKeyOrder(IEnumerable<PlanItemRow> items) => items.OrderBy(i => (int)i.Item.Group).ThenBy(i => i.Item.Rank);

    [GeneratedRegex(@"^model P\(win\) ([0-9]+(?:\.[0-9]+)?), advisory only$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex NoteShape();
}
