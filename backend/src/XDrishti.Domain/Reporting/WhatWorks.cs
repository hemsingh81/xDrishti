using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// The answer to "what works, in which basket, on which timeframe, with which exit" (P8-00 D5): the cells with enough sample ranked by the credible lower bound of expectancy, the rest listed after them with the reason, how many cells were looked at, and
/// when no cell qualifies the answer is <see cref="WhatWorksKind.NothingQualifies"/> with the counts. The text is deterministic and its numbers come from the cells.
/// </summary>
/// <param name="Kind">Ranked or nothing qualifies.</param>
/// <param name="Dimensions">The dimensions a cell is made of.</param>
/// <param name="Ranked">Cells with enough sample whose lower bound could be formed, best first (ties: more trades, then the key).</param>
/// <param name="NotRanked">Every other cell, most trades first.</param>
/// <param name="CellsLookedAt">All cells, ranked or not.</param>
/// <param name="MinTrades">The minimum sample for a ranking.</param>
/// <param name="LargestN">The most trades in any cell.</param>
/// <param name="PositiveLowerBound">How many ranked cells have a positive lower bound.</param>
/// <param name="Headline">The answer in one sentence.</param>
/// <param name="SelectionNote">The selection-effect sentence that always accompanies a ranking.</param>
/// <param name="UnavailableDimensions">Dimensions left out of the cells because nothing recorded them, with the reason.</param>
/// <param name="NonAdditive">A trade is in more than one cell (baskets overlap).</param>
/// <param name="Evidence">The evidence of the whole report.</param>
public sealed record WhatWorks(
    WhatWorksKind Kind,
    ValueList<ReportDimension> Dimensions,
    ValueList<RankedCell> Ranked,
    ValueList<UnrankedCell> NotRanked,
    int CellsLookedAt,
    int MinTrades,
    int LargestN,
    int PositiveLowerBound,
    string Headline,
    string SelectionNote,
    ValueList<string> UnavailableDimensions,
    bool NonAdditive,
    ReportEvidence Evidence);
