namespace XDrishti.Domain.Filings;

/// <summary>The sidecar words of <see cref="FilingKind"/>.</summary>
public static class FilingKinds
{
    public static string Code(FilingKind kind) => kind switch
    {
        FilingKind.Results => "results",
        FilingKind.Shareholding => "shareholding",
        FilingKind.BoardMeeting => "board_meeting",
        FilingKind.Announcement => "announcement",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParse(string? code, out FilingKind kind)
    {
        switch (code)
        {
            case "results": kind = FilingKind.Results; return true;
            case "shareholding": kind = FilingKind.Shareholding; return true;
            case "board_meeting": kind = FilingKind.BoardMeeting; return true;
            case "announcement": kind = FilingKind.Announcement; return true;
            default: kind = default; return false;
        }
    }
}
