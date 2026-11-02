namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One stored evaluation (P13-05): the row of <c>research.extension_evaluations</c>. The JSON texts never hold a number where a value is unavailable. Insert-only.</summary>
/// <param name="EvaluationId">SHA-256 (hex) of the record's content: the same inputs give the same id, so a re-run writes nothing new.</param>
/// <param name="Family">The ledger family, <c>ext:&lt;version&gt;</c>.</param>
/// <param name="Protocol">X for the extension comparison; P15-10 stores its other protocols in the same table.</param>
/// <param name="ExtensionVersion">The extension version, or null for a record that is about no extension.</param>
/// <param name="VersionKey">What was evaluated: the version and the names used; the hold-out is read once per family and key.</param>
/// <param name="BaseVersion">The base feature version, <c>tf-1</c>.</param>
/// <param name="RecipeHash">The recipe name and options hash, hashed.</param>
/// <param name="TrainingDataId">The training data id, or null.</param>
/// <param name="TrialHash">The ledger trial hash, or null.</param>
/// <param name="Trials">N: the distinct trials of the family.</param>
/// <param name="Factor">The multiple-testing factor.</param>
/// <param name="FoldUnit">month, quarter or none.</param>
/// <param name="FoldsTotal">Folds used.</param>
/// <param name="FoldsEvaluable">Folds with both thresholds.</param>
/// <param name="Rows">Paired candidates in the evaluable folds.</param>
/// <param name="Flips">Rows where A and B decide differently, or null.</param>
/// <param name="EstimatesJson">The estimates.</param>
/// <param name="Verdict">KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE.</param>
/// <param name="Missing">The named missing quantities.</param>
/// <param name="Limits">The stated limits.</param>
/// <param name="EvidenceJson">The folds and bounds.</param>
/// <param name="InputsJson">The options and sources the result depends on.</param>
/// <param name="HoldOutRead">Whether this record is the one reading of the hold-out.</param>
public sealed record ExtensionEvaluationRecord(
    string EvaluationId,
    string Family,
    string Protocol,
    string? ExtensionVersion,
    string VersionKey,
    string BaseVersion,
    string RecipeHash,
    string? TrainingDataId,
    string? TrialHash,
    int Trials,
    double Factor,
    string FoldUnit,
    int FoldsTotal,
    int FoldsEvaluable,
    int Rows,
    int? Flips,
    string EstimatesJson,
    string Verdict,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Limits,
    string EvidenceJson,
    string InputsJson,
    bool HoldOutRead);
