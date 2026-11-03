using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Learning;

/// <summary>
/// The family <c>ext:llm-1</c> (P15-10): protocol L reads the grade of the newest accepted eval run (set D) and stores it as a verdict that links the run; protocol X for <c>llm-1</c> is then NOT_EVALUABLE, with <c>protocol_L_not_passed</c> named when the flag has not passed and with the rows
/// that P15-10 has not built named when it has. The grade is read, never recomputed.
/// </summary>
internal sealed class FlagEvaluation(IFlagGradeSource grades, EvaluationWriter writer, ILogger<FlagEvaluation> logger)
{
    private const string Version = "llm-1";

    public async ValueTask<Result<ExtensionEvaluationOutcome>> RunAsync(CancellationToken cancellationToken)
    {
        var grade = await grades.NewestAsync(cancellationToken);
        var trials = await writer.CountAsync(ExtensionFamilies.Flag, cancellationToken);
        var flag = ProtocolL.Evaluate(grade);
        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["eval_run_id"] = grade?.EvalRunId ?? "none",
            ["fingerprint"] = grade?.Fingerprint ?? "none",
            ["model_hash"] = grade?.ModelHash ?? "none",
            ["prompt_hash"] = grade?.PromptHash ?? "none",
        };
        var recipe = ExtensionFamilies.Sha256Hex("protocol-l/1;min_labels=" + ProtocolL.MinLabels);
        // The two records are stored one after the other. A crash between them leaves a grade without its comparison; the P15 phase check reads that as open ("the comparison (protocol X) is not stored"), never as a resolved family (ADR 0190).
        var records = new List<EvaluatedRecord>();
        foreach (var (identity, outcome) in new[]
        {
            (new ProtocolIdentity(ExtensionFamilies.Flag, ProtocolL.Protocol, Version, Version + ":L", "none", recipe, null, null, trials, "none", sources, false), flag),
            (new ProtocolIdentity(ExtensionFamilies.Flag, ProtocolX.Protocol, Version, Version + ":X", TradeFeatureCatalog.Version, recipe, null, null, trials, "quarter", sources, false), ProtocolX.NotEvaluable(Gate(flag))),
        })
        {
            var record = identity.ToRecord(outcome);
            var stored = await writer.StoreAsync(record, cancellationToken);
            if (stored.IsFailure)
            {
                return Result.Failure<ExtensionEvaluationOutcome>(stored.Error);
            }

            ExtensionEvaluationLog.Stored(logger, record.Family, record.Protocol, record.VersionKey, record.Verdict, record.Rows, stored.Value.Stored);
            records.Add(stored.Value);
        }

        return Result.Success(new ExtensionEvaluationOutcome(ExtensionFamilies.Flag, records, null));
    }

    private static string[] Gate(ProtocolOutcome flag) => flag.Verdict == ExtensionVerdict.Keep
        ? ["no_extraction: the llm-1 feature rows are not built (P15-10)"]
        : [ProtocolL.NotPassed + ": " + (flag.Missing.Count > 0 ? string.Join("; ", flag.Missing) : "set D failed")];
}
