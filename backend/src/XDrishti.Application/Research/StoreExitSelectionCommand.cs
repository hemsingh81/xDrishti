using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>
/// Stores an exit choice in the result store (spec P4-06, D10). The comparison it was made from must be stored already (<see cref="StoreComparisonCommand"/>); the choice is refused otherwise. The record text and its digest come from the caller (the verb),
/// who made them from the record it printed.
/// </summary>
/// <param name="Selection">The choice.</param>
/// <param name="ComparisonKey">The key of the stored comparison.</param>
/// <param name="StrategyId">The strategy.</param>
/// <param name="Timeframe">The timeframe code of the run.</param>
/// <param name="Side">The side(s) of the cell.</param>
/// <param name="Style">The style of the cell.</param>
/// <param name="RecordJson">The record as <c>exit-selection.json</c>.</param>
/// <param name="RecordSha256">SHA-256 of the UTF-8 bytes of <paramref name="RecordJson"/>.</param>
/// <param name="Actor">Who stores it (<c>cli:simulate</c> for the verb).</param>
public sealed record StoreExitSelectionCommand(ExitSelection? Selection, string ComparisonKey, string StrategyId, string Timeframe, string Side, string Style, string RecordJson, string RecordSha256, string Actor) : ICommand;
