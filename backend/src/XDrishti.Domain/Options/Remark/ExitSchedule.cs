namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// When the position leaves on the cash exit's terms (P14-08 decisions 4 and 5): the bar time the exit is priced at, the instant it is priced for (a close is priced at the end of its bar), whether it is a close, the exit word, and the cash exit minute.
/// Shared by the single option and the structures (P14-10): a hole in the bars never moves the exit to another bar.
/// </summary>
public readonly record struct ExitSchedule(DateTimeOffset BarTime, DateTimeOffset PricedAt, bool AtClose, string Reason, DateTimeOffset ExitMinute)
{
    /// <summary>The bars of the grid from the entry up to and including the exit bar: the index after the last one to check.</summary>
    public int End(BarGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var at = grid.FirstAtOrAfter(BarTime);
        return at < 0 ? grid.Times.Count : (AtClose && grid.Times[at] == BarTime ? at + 1 : at);
    }
}
