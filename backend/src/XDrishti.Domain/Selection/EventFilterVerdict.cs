namespace XDrishti.Domain.Selection;

/// <summary>
/// The newest stored protocol-F verdict (P15-10) for the event-risk filter, as the auto option reads it. <see cref="State"/> is one of <see cref="Keep"/>, <see cref="Drop"/>, <see cref="Inconclusive"/>, <see cref="NotEvaluable"/>; anything else, and the absence of a verdict, is
/// <c>unavailable</c> and never counts as KEEP.
/// </summary>
/// <param name="State">The verdict word.</param>
/// <param name="RuleVersion">The version of the filter rules (<c>evf_version</c>) the verdict was made for.</param>
/// <param name="Reference">The reference of the verdict and its evidence block; required for KEEP.</param>
/// <param name="ResultsWithinDays">The window N the verdict was made for; a turn-on needs it to equal the window in force when the rule decides, so a switch changed in between cannot be turned on from another window's verdict (null never matches).</param>
/// <param name="HoldOutConfirmed">True when the reserved seasons were read once for the same development population and confirmed the KEEP; only a confirmed KEEP can turn the filter on automatically.</param>
public sealed record EventFilterVerdict(string State, string RuleVersion, string? Reference, bool HoldOutConfirmed = false, int? ResultsWithinDays = null)
{
    public const string Keep = "KEEP";

    public const string Drop = "DROP";

    public const string Inconclusive = "INCONCLUSIVE";

    public const string NotEvaluable = "NOT_EVALUABLE";
}
