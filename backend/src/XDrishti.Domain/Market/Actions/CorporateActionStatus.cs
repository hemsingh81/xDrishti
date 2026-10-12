namespace XDrishti.Domain.Market.Actions;

/// <summary>Where a corporate action stands. Only <see cref="Applied"/> actions adjust prices; nothing is ever deleted — a declined action is <see cref="Ignored"/>.</summary>
public enum CorporateActionStatus
{
    /// <summary>Found by the detection (or waiting for the owner); changes nothing until the owner confirms it.</summary>
    Suggested,

    /// <summary>Counts: adjusted reads multiply the bars before its ex-date by its factor.</summary>
    Applied,

    /// <summary>The owner declined it; it is kept so that the detection does not suggest it again.</summary>
    Ignored,
}

/// <summary>The codes of <see cref="CorporateActionStatus"/> used in the database and the API: <c>suggested</c> · <c>applied</c> · <c>ignored</c>.</summary>
public static class CorporateActionStatusCodes
{
    public const string Suggested = "suggested";
    public const string Applied = "applied";
    public const string Ignored = "ignored";

    public static string ToCode(this CorporateActionStatus status) => status switch
    {
        CorporateActionStatus.Suggested => Suggested,
        CorporateActionStatus.Applied => Applied,
        CorporateActionStatus.Ignored => Ignored,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown corporate action status."),
    };

    public static bool TryParse(string? code, out CorporateActionStatus status)
    {
        switch (code)
        {
            case Suggested:
                status = CorporateActionStatus.Suggested;
                return true;
            case Applied:
                status = CorporateActionStatus.Applied;
                return true;
            case Ignored:
                status = CorporateActionStatus.Ignored;
                return true;
            default:
                status = default;
                return false;
        }
    }
}
