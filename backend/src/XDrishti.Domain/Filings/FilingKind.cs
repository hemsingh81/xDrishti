namespace XDrishti.Domain.Filings;

/// <summary>What an exchange filing is (a closed list, P15-01). The code strings are the sidecar's <c>kind</c> values.</summary>
public enum FilingKind
{
    Results,
    Shareholding,
    BoardMeeting,
    Announcement,
}
