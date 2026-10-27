namespace XDrishti.Domain.Assistant.Drafting;

/// <summary>The states of a strategy draft (spec P9-07 Contract). <c>drafting</c> is held in memory only; the stored row of a finished job is one of valid, invalid, declined or failed, and the owner's decision adds confirmed or discarded.</summary>
public static class DraftStatus
{
    public const string Drafting = "drafting";
    public const string Valid = "valid";
    public const string Invalid = "invalid";
    public const string Declined = "declined";
    public const string Failed = "failed";
    public const string Confirmed = "confirmed";
    public const string Discarded = "discarded";
}
