using XDrishti.Domain.Common;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// Protocol X of P15-10, the adapter over the P13-05 comparison core for the filings extensions: quarterly folds, season resampling, 1,000 paired candidates, no flip floor (<see cref="ExtensionComparisonOptions.ForFilings"/>). The gate on the extraction records comes first
/// (<see cref="ExtensionGate"/>); the core then refuses unless the trial is already in the ledger. Pure.
/// </summary>
public static class ProtocolX
{
    /// <summary>The protocol letter.</summary>
    public const string Protocol = ExtensionReport.Protocol;

    /// <summary>The options of the filings comparison, with the recipe and label identity and the first day of the reserved hold-out.</summary>
    public static ExtensionComparisonOptions Options(string recipeOptionsHash, string labelRuleVersion, DateOnly? holdOutStart) =>
        ExtensionComparisonOptions.ForFilings() with { RecipeOptionsHash = recipeOptionsHash, LabelRuleVersion = labelRuleVersion, HoldOutStart = holdOutStart };

    /// <summary>The comparison, after the caller has appended the trial's ledger row and read the family's hashes.</summary>
    public static Result<ExtensionReport> Compare(IReadOnlyList<ValidationRow> rows, int baseWidth, ExtensionGroup group, IScorerFactory recipe, IReadOnlyCollection<string> ledgerHashes, IReadOnlyList<DateOnly> sessions, ExtensionComparisonOptions options) =>
        ExtensionComparison.Compare(rows, baseWidth, group, recipe, ledgerHashes, sessions, options);

    /// <summary>The verdict of a gated or thin evaluation: NOT_EVALUABLE with the reasons named; the first word of each reason is its closed code.</summary>
    public static ProtocolOutcome NotEvaluable(IReadOnlyList<string> reasons, int rows = 0) =>
        EvidenceBlock.NotEvaluable("X", reasons, ["Advisory research only: a KEEP is a record; nothing scores with the set.", "Gross R unless the owner's checked charge sheet exists (ISS-25)."], rows);

    /// <summary>
    /// The record with the sources it depends on added to its inputs (the lag, the parser version, the calendar hash, the filings watermark, the run keys) and the id recomputed over them, so that rows built from other inputs are another record. The core's own fields are untouched.
    /// </summary>
    public static ExtensionEvaluationRecord WithSources(ExtensionEvaluationRecord record, IReadOnlyDictionary<string, string> sources)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(sources);
        using var json = new ExtensionJson();
        json.Object();
        foreach (var pair in sources.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            json.Text(pair.Key, pair.Value);
        }

        json.EndObject();
        var text = json.Finish();
        var inputs = record.InputsJson[..^1] + ",\"sources\":" + text + "}";
        return record with { InputsJson = inputs, EvaluationId = ExtensionReport.Hex(string.Join('\n', "extension-evaluation/2", record.EvaluationId, text)) };
    }

    /// <summary>
    /// The record with the counts per trade style added to its estimates (counts only: rows and paired candidates of the intraday and swing trades), so the pooled comparison reports what each style contributed; no per-style verdict is made. The id is recomputed over the counts.
    /// </summary>
    public static ExtensionEvaluationRecord WithStyleCounts(ExtensionEvaluationRecord record, IReadOnlyDictionary<string, (int Rows, int Candidates)> styles)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(styles);
        using var json = new ExtensionJson();
        json.Object();
        foreach (var pair in styles.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            json.Object(pair.Key).Count("rows", pair.Value.Rows).Count("candidates", pair.Value.Candidates).EndObject();
        }

        json.EndObject();
        var text = json.Finish();
        return record with { EstimatesJson = record.EstimatesJson[..^1] + ",\"style_counts\":" + text + "}", EvaluationId = ExtensionReport.Hex(string.Join('\n', "extension-evaluation/3", record.EvaluationId, text)) };
    }
}
