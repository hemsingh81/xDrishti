using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Reading and writing the stored form of a counterfactual (spec P8-12): the kind, the input hash and the JSON of the view.</summary>
internal static class CounterfactualStore
{
    /// <summary>The kind key of the counterfactual in <c>research.learning_reports</c>.</summary>
    public const string Kind = "counterfactual";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    /// <summary>The view of a stored row with the row's own id, author and time, and how many stored rows share its runs.</summary>
    public static CounterfactualReportView? ViewOf(StoredLearningReport stored, int sameRunsStored)
    {
        var view = JsonSerializer.Deserialize<CounterfactualReportView>(stored.Report.ReportJson, Json);
        return view is null ? null : view with { Id = stored.Id, BuiltBy = stored.Report.BuiltBy, CreatedAt = stored.Report.CreatedAt, SameRunsStored = sameRunsStored };
    }

    public static CounterfactualHeaderView? HeaderOf(StoredLearningReport stored, int sameRunsStored) =>
        ViewOf(stored, sameRunsStored) is { } v
            ? new CounterfactualHeaderView(stored.Id, v.Outcome, stored.Report.AsOf, v.Basis, stored.Report.CreatedAt, stored.Report.BuiltBy, v.Windows.Count, v.JudgedWindows, sameRunsStored)
            : null;

    // shortcut: the same-runs count reads the newest 50 stored reports (JSON included) on every read; past about 50 stored reports add a COUNT(*) by sources_json to the port.
    public static int SameRuns(StoredLearningReport of, IEnumerable<StoredLearningReport> all) => all.Count(r => string.Equals(r.Report.SourcesJson, of.Report.SourcesJson, StringComparison.Ordinal));

    /// <summary>
    /// SHA-256 hex of <c>counterfactual|v1|</c>, the sorted <c>runKey:style:exit</c>, every counterfactual knob, the hold-out months, the validation options and the model recipe line of the limits (it names the trainer, its parameters and the training set).
    /// </summary>
    public static string InputHash(EvaluateCounterfactualQuery q, CounterfactualReport report)
    {
        var c = CultureInfo.InvariantCulture;
        var o = q.Counterfactual;
        var v = q.Validation;
        var recipe = report.Limits.FirstOrDefault(l => l.StartsWith("Model recipe:", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The counterfactual report has no 'Model recipe:' limit; the input hash cannot name the trainer and the training set.");
        var sources = string.Join(',', q.Sources.Select(s => $"{s.RunKey}:{s.Style}:{s.Exit}").Order(StringComparer.Ordinal));
        var text = string.Create(c, $"counterfactual|v1|{sources}|{o.FrozenMonths}|{o.RetrainMonths}|{o.MinWindowRows}|{o.MinJudgedWindows}|{o.MinGainLogLoss:R}|{o.SignificanceZ:R}|{o.Trials}|{o.PolicyTolerance:R}|holdout {q.HoldOutMonths}|{v.MinTrainMonths}|{v.EmbargoDays}|{v.MinTrainTrades}|{recipe}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
