namespace XDrishti.Domain.Assistant.Grounding;

/// <summary>The codes of <see cref="GroundingStatus"/> as stored (<c>ai.messages.grounding_status</c>) and sent.</summary>
public static class GroundingStatusCodes
{
    public static string ToCode(this GroundingStatus status) => status switch
    {
        GroundingStatus.Grounded => "grounded",
        GroundingStatus.Ungrounded => "ungrounded",
        GroundingStatus.NoNumbers => "no_numbers",
        _ => "not_checked",
    };
}
