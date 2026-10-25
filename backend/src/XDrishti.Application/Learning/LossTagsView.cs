namespace XDrishti.Application.Learning;

/// <summary>The loss tags of the report population, as the screen shows them (P8-13). Computed on read from the closed trades of stored runs; nothing is stored or applied.</summary>
/// <param name="State"><c>empty</c> when no run can be used, else <c>ok</c>.</param>
/// <param name="TradesRead">Closed trades read.</param>
/// <param name="Losses">Of them, losses (R below 0).</param>
/// <param name="Cells">One entry per cell.</param>
/// <param name="Worst">The worst losses, at most 25.</param>
/// <param name="Comparisons">Cell and tag pairs the growing-tag detector compared.</param>
/// <param name="ExpectedChanceFlags">Flags expected by chance alone at an assumed 5 per cent per comparison.</param>
/// <param name="NotBuilt">What the data cannot support yet, verbatim from the analyzer.</param>
/// <param name="Evidence">The runs behind the numbers.</param>
/// <param name="Limits">Sentences that must be printed with the result.</param>
public sealed record LossTagsView(
    string State,
    int TradesRead,
    int Losses,
    IReadOnlyList<CellLossTagsView> Cells,
    IReadOnlyList<WorstLossView> Worst,
    int Comparisons,
    double ExpectedChanceFlags,
    IReadOnlyList<string> NotBuilt,
    LossTagsEvidenceView Evidence,
    IReadOnlyList<string> Limits);

/// <summary>One cell.</summary>
/// <param name="CellKey">The cell.</param>
/// <param name="Trades">Closed trades.</param>
/// <param name="Losses">Losses.</param>
/// <param name="Untagged">Losses with no tag.</param>
/// <param name="Tags">Every tag kind with its count (zeros included).</param>
/// <param name="GrowingState"><c>evaluated</c> or <c>not_evaluable</c> (too few losses for two windows).</param>
/// <param name="GrowingReason">Why not evaluable; null otherwise.</param>
/// <param name="Growing">Growing tags.</param>
public sealed record CellLossTagsView(string CellKey, int Trades, int Losses, int Untagged, IReadOnlyList<TagCountView> Tags, string GrowingState, string? GrowingReason, IReadOnlyList<GrowingTagView> Growing);

/// <summary>A tag kind in a cell.</summary>
/// <param name="Kind">The kind, snake case.</param>
/// <param name="Count">Losses with the tag.</param>
/// <param name="Share">Of the cell's losses; null when the cell has no loss. Tags overlap, shares do not add to 1.</param>
/// <param name="LostR">Sum of the (negative) R of the losses carrying the tag.</param>
public sealed record TagCountView(string Kind, int Count, double? Share, double LostR);

/// <summary>A tag whose share rose between two windows of losses.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Recent">Losses with the tag in the latest window.</param>
/// <param name="Earlier">Losses with the tag in the window before.</param>
/// <param name="Window">Losses per window.</param>
public sealed record GrowingTagView(string Kind, int Recent, int Earlier, int Window);

/// <summary>One of the worst losses.</summary>
/// <param name="Key">The trade key.</param>
/// <param name="CellKey">The cell.</param>
/// <param name="R">The R of the loss.</param>
/// <param name="Tags">Its tags with the numbers behind each.</param>
public sealed record WorstLossView(string Key, string CellKey, double R, IReadOnlyList<LossTagDetailView> Tags);

/// <summary>A tag on one loss.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Detail">The numbers behind it, as text.</param>
public sealed record LossTagDetailView(string Kind, string Detail);

/// <summary>The evidence block of the loss tags.</summary>
/// <param name="Basis">gross, net or mixed (per run).</param>
/// <param name="N">Closed trades read.</param>
/// <param name="Runs">The runs used.</param>
/// <param name="DefaultRunSet">The caller named no run.</param>
/// <param name="Skipped">Runs left out of the default set, each with the reason.</param>
/// <param name="UnavailableInputs">Inputs a figure would need that do not exist.</param>
public sealed record LossTagsEvidenceView(string Basis, int N, IReadOnlyList<Reporting.RunUsedView> Runs, bool DefaultRunSet, IReadOnlyList<string> Skipped, IReadOnlyList<string> UnavailableInputs);
