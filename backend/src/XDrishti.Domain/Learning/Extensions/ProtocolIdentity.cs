using System.Globalization;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>What a stored record is about (P15-10): the family and protocol, the version key under which the hold-out is read once, the trial and its ledger count, and the sources the result depends on (versions, hashes, run ids), as sorted name and value pairs.</summary>
/// <param name="Family">filter:results, ext:ev-1, ext:fu-1, ext:llm-1 or pead.</param>
/// <param name="Protocol">F, X, P or L.</param>
/// <param name="ExtensionVersion">The extension version, or null.</param>
/// <param name="VersionKey">What was evaluated; the hold-out is read once per family and key.</param>
/// <param name="BaseVersion">tf-1 for the comparison, <c>none</c> otherwise.</param>
/// <param name="RecipeHash">The hash of the recipe or the protocol's options (64 hex).</param>
/// <param name="SourceId">The training-data id, or null.</param>
/// <param name="TrialHash">The ledger trial hash, or null when no trial was counted.</param>
/// <param name="Trials">N, the distinct trials of the family in the ledger (at least 1).</param>
/// <param name="FoldUnit">month, quarter or none.</param>
/// <param name="Sources">The inputs: versions, calendar hash, watermark, run keys, eval run id.</param>
/// <param name="HoldOutRead">Whether the record is the one reading of the hold-out.</param>
public sealed record ProtocolIdentity(
    string Family,
    string Protocol,
    string? ExtensionVersion,
    string VersionKey,
    string BaseVersion,
    string RecipeHash,
    string? SourceId,
    string? TrialHash,
    int Trials,
    string FoldUnit,
    IReadOnlyDictionary<string, string> Sources,
    bool HoldOutRead)
{
    /// <summary>The row to store: the id is the SHA-256 of the canonical inputs and result, so the same inputs give the same id and a re-run writes nothing.</summary>
    public ExtensionEvaluationRecord ToRecord(ProtocolOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var factor = MultipleTesting.Factor(Trials);
        using var inputs = new ExtensionJson();
        inputs.Object().Text("family", Family).Text("protocol", Protocol).Text("version_key", VersionKey).Count("trials", Trials).Number("factor", factor).Object("sources");
        foreach (var pair in Sources.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            inputs.Text(pair.Key, pair.Value);
        }

        inputs.EndObject().EndObject();
        var inputsJson = inputs.Finish();
        var id = ExtensionReport.Hex(string.Join('\n', "extension-evaluation/1", Protocol, Family, VersionKey, TrialHash ?? "-", SourceId ?? "-", HoldOutRead ? "holdout" : "development", outcome.EstimatesJson, outcome.EvidenceJson, inputsJson));
        return new ExtensionEvaluationRecord(
            id, Family, Protocol, ExtensionVersion, VersionKey, BaseVersion, RecipeHash, SourceId, TrialHash, Trials, factor, FoldUnit, outcome.FoldsTotal, outcome.FoldsEvaluable, outcome.Rows, null,
            outcome.EstimatesJson, outcome.Verdict.ToWord(), outcome.Missing, outcome.Limits, outcome.EvidenceJson, inputsJson, HoldOutRead);
    }

    /// <summary>The same text for display.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Family} {Protocol} {VersionKey} N={Trials}");
}
