namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The honest answer of a feature-set comparison (P13-05, ADR 0080 words).</summary>
public enum ExtensionVerdict
{
    /// <summary>The extension clearly improves out-of-sample net R per candidate, beyond the margin widened for the looks.</summary>
    Keep,

    /// <summary>The extension is not a keep and its gain cannot reach the minimum even at the upper bound.</summary>
    Drop,

    /// <summary>Evaluable, but neither a keep nor a drop.</summary>
    Inconclusive,

    /// <summary>Not enough folds, candidates or an interval: the missing quantities are named.</summary>
    NotEvaluable,
}

/// <summary>The words of <see cref="ExtensionVerdict"/> as stored.</summary>
public static class ExtensionVerdictExtensions
{
    /// <summary>KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE.</summary>
    public static string ToWord(this ExtensionVerdict verdict) => verdict switch
    {
        ExtensionVerdict.Keep => "KEEP",
        ExtensionVerdict.Drop => "DROP",
        ExtensionVerdict.Inconclusive => "INCONCLUSIVE",
        _ => "NOT_EVALUABLE",
    };
}
