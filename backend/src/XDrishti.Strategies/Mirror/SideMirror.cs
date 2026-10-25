using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Mirror;

/// <summary>
/// The mirror of a whole BUY side (spec P3-02e, <i>Side</i>): setup, trigger and invalidate item by item with the mode kept; the entry kept; the stop kind swapped (<c>swing_low</c> and <c>swing_high</c>, <c>bar_low</c> and
/// <c>bar_high</c>) with every field kept; the exits and overlays kept (the walker reads the direction from the side). A condition that does not parse is carried over unchanged, so the compile reports its problems.
/// </summary>
public static class SideMirror
{
    /// <summary>The mirrored side, or the problems (paths <c>sell.trigger[1]</c>) when any condition has no single mirror; the rules are null then.</summary>
    public static (SideRules? Rules, IReadOnlyList<LocatedProblem> Problems) Mirror(SideRules buy, IReadOnlySet<string> parameters)
    {
        ArgumentNullException.ThrowIfNull(buy);
        ArgumentNullException.ThrowIfNull(parameters);
        var problems = new List<LocatedProblem>();
        var setup = List(buy.Setup, "sell.setup", parameters, problems);
        var trigger = List(buy.Trigger, "sell.trigger", parameters, problems);
        var invalidate = List(buy.Invalidate, "sell.invalidate", parameters, problems);
        if (problems.Count > 0)
        {
            return (null, problems);
        }

        return (buy with { Setup = setup, Trigger = trigger, Invalidate = invalidate, Stop = buy.Stop is { } stop ? stop with { Kind = Opposite(stop.Kind) } : null }, problems);
    }

    private static StopKind Opposite(StopKind kind) => kind switch
    {
        StopKind.SwingLow => StopKind.SwingHigh,
        StopKind.SwingHigh => StopKind.SwingLow,
        StopKind.BarLow => StopKind.BarHigh,
        StopKind.BarHigh => StopKind.BarLow,
        _ => kind,
    };

    private static ConditionList? List(ConditionList? list, string path, IReadOnlySet<string> parameters, List<LocatedProblem> problems)
    {
        if (list?.Items is null)
        {
            return list;
        }

        var items = new List<string>(list.Items.Count);
        for (var i = 0; i < list.Items.Count; i++)
        {
            if (list.Items[i] is null)
            {
                items.Add(list.Items[i]);
                continue;
            }

            var result = ConditionMirror.Mirror(list.Items[i], parameters);
            if (result.Problem is { Severity: ProblemSeverity.Error } problem && !result.Unreadable)
            {
                problems.Add(new LocatedProblem(path + "[" + i + "]", problem));
            }

            items.Add(result.Text ?? list.Items[i]);
        }

        return list with { Items = items };
    }
}
