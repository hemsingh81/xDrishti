namespace XDrishti.Domain.Research;

/// <summary>
/// What the exit choice is given (P4-06, D1): the candidates in the order of the comparison (the first is the default exit), the trades of the ungated comparison walk, the sessions of the trading calendar, the cell and the options. No clock, no random.
/// </summary>
/// <param name="Cell">The cell: strategy, timeframe, side, style, regime, for the record.</param>
/// <param name="Unit"><c>RFilled</c> (gross) or <c>RNet</c> (costed).</param>
/// <param name="OverlapNotGated">The comparison walks were not gated (a swing cell).</param>
/// <param name="EntriesKey">The entries key of the comparison.</param>
/// <param name="Candidates">Two or more candidates, in order; the first is the default exit.</param>
/// <param name="Trades">The closed trades of every candidate.</param>
/// <param name="Sessions">The sessions of the trading calendar, ascending and distinct, covering the window.</param>
/// <param name="Options">The options.</param>
public sealed record SelectionInput(
    string Cell,
    string Unit,
    bool OverlapNotGated,
    string EntriesKey,
    IReadOnlyList<ExitCandidate> Candidates,
    IReadOnlyList<SelectionTrade> Trades,
    IReadOnlyList<DateOnly> Sessions,
    SelectionOptions Options);
