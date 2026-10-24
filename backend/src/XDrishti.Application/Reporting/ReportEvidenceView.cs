namespace XDrishti.Application.Reporting;

/// <summary>
/// The evidence block every report carries (P8-00 D1): the basis, the population, the date range, what is not available and why, the runs behind the numbers (the "Data used" panel) and the honest limits as fields. A screen renders these; none of this wording
/// is page prose.
/// </summary>
/// <param name="Basis">gross or net R.</param>
/// <param name="N">Trades in the population.</param>
/// <param name="FirstDate">Earliest entry date; null without a trade.</param>
/// <param name="LastDate">Latest exit date; null without a trade.</param>
/// <param name="Provisional">A contributing run is marked provisional.</param>
/// <param name="RunKeys">The runs behind the numbers, sorted.</param>
/// <param name="Runs">The same runs with their facts (the Data used panel).</param>
/// <param name="DefaultRunSet">The run set was not named by the caller: the newest run of each strategy and universe.</param>
/// <param name="Limits">The limits that always apply, as text.</param>
/// <param name="UnavailableInputs">Inputs a figure would need that do not exist, each with the reason.</param>
/// <param name="DuplicateTradesDropped">Identical repeated records of one trade in one run, counted once.</param>
/// <param name="OpenAtEndLeftOut">Trades still open at the end of the data in the runs used, which no report counts.</param>
/// <param name="NetBasisConfirmation">What the owner confirmed, for a net report; null for a gross one.</param>
public sealed record ReportEvidenceView(
    string Basis,
    int N,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    bool Provisional,
    IReadOnlyList<string> RunKeys,
    IReadOnlyList<RunUsedView> Runs,
    bool DefaultRunSet,
    IReadOnlyList<string> Limits,
    IReadOnlyList<string> UnavailableInputs,
    int DuplicateTradesDropped,
    int OpenAtEndLeftOut,
    string? NetBasisConfirmation);
