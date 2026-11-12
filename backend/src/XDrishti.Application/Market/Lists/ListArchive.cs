using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.Lists;

/// <summary>
/// NSE's archive host and path of the dated F&amp;O-ban file (DATA-01, ADR 0274). <b>UNVERIFIED until <c>xd data lists-probe</c> has run</b>. This is the only place that names the path; the owner puts a matching entry on the allow-list (ADR 0195), the app never does.
/// ASM/GSM has no dated archive known to this slice: it stays forward-only.
/// </summary>
public static class ListArchive
{
    public const string Host = "nsearchives.nseindia.com";

    /// <summary>Source value of a back-filled row, to tell it from a forward (daily) one.</summary>
    public const string SourceArchive = "archive";

    /// <summary>Source value of a row written from the daily forward fetch of the ban file.</summary>
    public const string SourceDaily = "daily";

    public static string BanPath(DateOnly date) => $"/archives/fo/sec_ban/fo_secban_{date:ddMMyyyy}.csv";
}

/// <summary>The closed list of refusals of the list-history slice. Codes are part of the contract.</summary>
public static class ListHistoryErrors
{
    public const string DisabledCode = "history.disabled";

    public const string NotAllowedCode = "history.not_allowed";

    public const string BadRangeCode = "history.bad_range";

    public static Error Disabled() => Error.Conflict(DisabledCode, "The dated list history fetch is off (Data:History:Lists:Enabled). Nothing was requested.");

    public static Error NotAllowed(string detail) => Error.Forbidden(NotAllowedCode, $"{detail} The owner must add the endpoint to the master-data allow-list (see ADR 0274 and 0195); the app never edits it.");

    public static Error BadRange(string detail) => Error.Validation(BadRangeCode, detail);
}
