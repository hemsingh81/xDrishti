using System.Globalization;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The label of released data (HO-01 D7): a run, replay session or refresh that rests on trades exiting inside a released block carries <c>seen_holdout:&lt;start&gt;</c> for each such block. The result is allowed and labelled, and never counts as hold-out evidence.
/// </summary>
public static class SeenHoldOut
{
    /// <summary>The label prefix.</summary>
    public const string Prefix = "seen_holdout:";

    /// <summary>The label of the released block that starts on <paramref name="blockStart"/>.</summary>
    public static string Label(DateOnly blockStart) => Prefix + blockStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The labels of the released blocks (start, end) that the window <paramref name="from"/> to <paramref name="to"/> reaches, in start order.</summary>
    public static IReadOnlyList<string> Labels(IEnumerable<(DateOnly Start, DateOnly End)> releasedBlocks, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(releasedBlocks);
        return [.. releasedBlocks.Where(b => from <= b.End && to >= b.Start).OrderBy(b => b.Start).Select(b => Label(b.Start))];
    }
}
