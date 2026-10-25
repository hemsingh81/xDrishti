using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Abstractions.Reporting;

/// <summary>
/// A stored run as the reports list it (P8-06): the facts of the stored header and the labels a report may use. Style and exit are not columns of a trade or of the stored run; the style is that of the stored strategy and the exit is the candidate id
/// of the stored comparison the run is a member of, each null when nothing records it (a report then says "not recorded" and never guesses, P8-00 D5).
/// </summary>
/// <param name="RunKey">The stored run key.</param>
/// <param name="StrategyId">The strategy the run replayed.</param>
/// <param name="UniverseKind">How the instruments were chosen, <c>instrumentlist</c> or <c>basket</c>.</param>
/// <param name="UniverseLabel">The stored label of the universe (what it is, for example members as of today).</param>
/// <param name="Universe">The identity of the universe two runs are compared by: <c>basket:&lt;id&gt;</c> for a basket run (the basket the trades carry), <c>list:&lt;label&gt;:&lt;instruments&gt;:&lt;ran&gt;</c> for an explicit list of instruments (the stored header does not keep the members, so two lists of the same size and label are not told apart).</param>
/// <param name="WindowStart">First India date of the run window.</param>
/// <param name="WindowEnd">Last India date of the run window.</param>
/// <param name="CostsSheetId">The charge sheet the run was costed with; null for a gross run.</param>
/// <param name="Style">The style of the stored strategy, <c>intraday</c> or <c>swing</c>; null when not recorded.</param>
/// <param name="StyleSource">Where the style comes from; null when not recorded.</param>
/// <param name="Exit">The exit candidate id of the stored comparison the run is a member of; null when the run is in none or in several that disagree.</param>
/// <param name="ExitSource">Where the exit comes from; null when not recorded.</param>
/// <param name="ClosedTrades">Trades closed within the run window: the ones a report counts.</param>
/// <param name="OpenAtEnd">Trades still open at the end of the data: never counted by a report.</param>
/// <param name="CreatedAt">When the run was stored.</param>
/// <param name="CostsRatesSha256">The hash of the rates of the charge sheet the run was costed with (the sheet is overwritten in place, so the id alone does not say which rates); null for a gross run.</param>
public sealed record ReportRunInfo(
    string RunKey,
    string StrategyId,
    string UniverseKind,
    string UniverseLabel,
    string Universe,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    string? CostsSheetId,
    string? Style,
    string? StyleSource,
    string? Exit,
    string? ExitSource,
    int ClosedTrades,
    int OpenAtEnd,
    DateTimeOffset CreatedAt,
    string? CostsRatesSha256 = null)
{
    /// <summary>The run's trades carry net R (a charge sheet was applied).</summary>
    public bool Costed => CostsSheetId is not null;

    /// <summary>The run as the engine takes it.</summary>
    public ReportRun ToReportRun() => new(RunKey, Universe, WindowStart, WindowEnd, Style, Exit, Costed);
}
