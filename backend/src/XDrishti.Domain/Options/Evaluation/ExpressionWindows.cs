using XDrishti.Domain.Research;

namespace XDrishti.Domain.Options.Evaluation;

/// <summary>
/// The out-of-sample population of protocol O (P14-09, decision 3): the entries inside the walk-forward test windows of the cash run, per side. The windows are the geometry of the Lab's exit choice (<see cref="ExitSelector.Windows"/> with the default options the full test uses: first test window nine months
/// after the first entry, three-month windows): they depend on the first and last entry of the side only. <b>Which candidate won a fold is not re-derived</b> (that needs every candidate's trades), so the windows are the Lab's geometry and the run is taken as the cell's chosen exit; the record says so.
/// </summary>
public static class ExpressionWindows
{
    /// <summary>The label of the rule, printed and stored with every record.</summary>
    public const string Rule = "oos_windows: geometry of the full test's walk-forward test windows per side (nine months train, three-month windows); the winner of each fold is not re-derived; first and last entry are those of the closed trades (a trade open at the end of the data is left out, as in the full test)";

    /// <summary>The test windows of every side, ordered by side and start.</summary>
    public static IReadOnlyList<ExpressionWindow> Of(IReadOnlyList<SideBounds> bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        var options = new SelectionOptions();
        var windows = new List<ExpressionWindow>();
        foreach (var side in bounds.OrderBy(b => b.Side, StringComparer.Ordinal))
        {
            var (list, _) = ExitSelector.Windows(side.First, side.Last, options);
            windows.AddRange(list.Select(w => new ExpressionWindow(side.Side, w.Start, w.End)));
        }

        return windows;
    }

    /// <summary>The parts of the windows dated before <paramref name="endExclusive"/> (everything when null); a window that starts at or after it is dropped.</summary>
    public static IReadOnlyList<ExpressionWindow> Before(IReadOnlyList<ExpressionWindow> windows, DateOnly? endExclusive)
    {
        ArgumentNullException.ThrowIfNull(windows);
        return endExclusive is not { } cut
            ? windows
            : [.. windows.Where(w => w.Start < cut).Select(w => w.End < cut ? w : w with { End = cut.AddDays(-1) })];
    }

    /// <summary>The parts of the windows dated on or after <paramref name="start"/>.</summary>
    public static IReadOnlyList<ExpressionWindow> From(IReadOnlyList<ExpressionWindow> windows, DateOnly start)
    {
        ArgumentNullException.ThrowIfNull(windows);
        return [.. windows.Where(w => w.End >= start).Select(w => w.Start >= start ? w : w with { Start = start })];
    }
}
