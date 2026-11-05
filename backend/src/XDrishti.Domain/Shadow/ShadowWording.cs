namespace XDrishti.Domain.Shadow;

/// <summary>The fixed sentences every shadow record and response carries (spec CF-08, ADR 0211). A test pins them.</summary>
public static class ShadowWording
{
    /// <summary>Said beside every shadow figure: the fills are the simulator's, so the record is about the rules' edge, not about execution (that is P11).</summary>
    public const string ModelledFills = "modelled fills; no queue, no latency";

    /// <summary>Said beside every edge count: it is not the paper count of armed tickets and does not change the P10 exit criterion.</summary>
    public const string SeparateCount = "Edge evidence from every nightly candidate, written before the open and graded after the close. It is a separate count from the paper trades of armed tickets, and the P10 exit criterion is untouched.";

    /// <summary>Said when the correlation behind the effective n is the placeholder.</summary>
    public const string PlaceholderRho = "the same-day correlation is a placeholder (0.3) until CF-01 estimates it";

    /// <summary>Stored in the session row of a night the writer did not run: the night is never filled in afterwards.</summary>
    public const string GapMeaning = "the writer did not run before the open of this session; it is never back-filled";
}
