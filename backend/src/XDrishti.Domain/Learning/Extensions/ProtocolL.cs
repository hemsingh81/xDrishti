using System.Globalization;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>Protocol L of P15-10, the flag: read, not recomputed. KEEP iff the newest accepted eval run's set D passed with at least 200 labelled rows; DROP iff a gating run with at least 200 rows failed; NOT_EVALUABLE otherwise, with the missing quantity named. Pure.</summary>
public static class ProtocolL
{
    /// <summary>The protocol letter.</summary>
    public const string Protocol = "L";

    /// <summary>The reason an extension that needs the flag names when the flag has not passed.</summary>
    public const string NotPassed = "protocol_L_not_passed";

    /// <summary>Labelled rows needed (200).</summary>
    public const int MinLabels = 200;

    /// <summary>The verdict for the newest grade, or null when there is no accepted eval run with set D.</summary>
    public static ProtocolOutcome Evaluate(FlagGrade? grade)
    {
        string[] limits = ["The flag is graded by the eval run, not here (P15-08, P9-09); this record links to it.", "Advisory research only: a KEEP is a record."];
        if (grade is null)
        {
            return EvidenceBlock.NotEvaluable(Protocol, ["no accepted eval run with set D"], limits);
        }

        if (grade.Labels < MinLabels)
        {
            return EvidenceBlock.NotEvaluable(Protocol, [string.Create(CultureInfo.InvariantCulture, $"labels {grade.Labels} of {MinLabels}")], limits, grade.Labels);
        }

        var failed = new List<string>();
        var missing = new List<string>();
        if (!grade.Passed)
        {
            if (grade.Gating)
            {
                failed.Add("set D failed on a gating run");
            }
            else
            {
                missing.Add("the newest run is not a gating run");
            }
        }

        var verdict = VerdictTable.Decide(missing, failed, true);
        using var estimates = new ExtensionJson();
        estimates.Object().Text("eval_run_id", grade.EvalRunId).Text("fingerprint", grade.Fingerprint).Count("labels", grade.Labels).Flag("gating", grade.Gating).Flag("passed", grade.Passed).Text("model_hash", grade.ModelHash).Text("prompt_hash", grade.PromptHash).EndObject();
        using var evidence = new ExtensionJson();
        evidence.Object().Text("protocol", Protocol);
        EvidenceBlock.Close(evidence, verdict, missing, failed);
        return new ProtocolOutcome(verdict, missing, failed, limits, estimates.Finish(), evidence.Finish(), 0, 0, grade.Labels);
    }
}
