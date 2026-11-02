namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The shared table of the protocols (P15-10, ADR 0080 words): a missing quantity makes it NOT_EVALUABLE; no failed rule makes it KEEP; a failed rule that cannot be repaired makes it DROP; the rest is INCONCLUSIVE. An unavailable input fails a rule and never supports DROP.</summary>
public static class VerdictTable
{
    /// <summary>The verdict for the missing and failed lists; <paramref name="unrepairable"/> is true when even the best case could not pass.</summary>
    public static ExtensionVerdict Decide(IReadOnlyCollection<string> missing, IReadOnlyCollection<string> failed, bool unrepairable)
    {
        ArgumentNullException.ThrowIfNull(missing);
        ArgumentNullException.ThrowIfNull(failed);
        return missing.Count > 0 ? ExtensionVerdict.NotEvaluable
            : failed.Count == 0 ? ExtensionVerdict.Keep
            : unrepairable ? ExtensionVerdict.Drop
            : ExtensionVerdict.Inconclusive;
    }
}
