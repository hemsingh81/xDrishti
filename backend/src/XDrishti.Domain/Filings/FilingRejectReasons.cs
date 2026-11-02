namespace XDrishti.Domain.Filings;

/// <summary>The closed list of why a file of the inbox is not accepted, and of the problems of the inbox itself (P15-01). Text only from this list is ever logged.</summary>
public static class FilingRejectReasons
{
    public const string NoSidecar = "no_sidecar";
    public const string InvalidSidecar = "invalid_sidecar";
    public const string BadName = "bad_name";
    public const string BadExtension = "bad_extension";
    public const string TooLarge = "too_large";
    public const string Empty = "empty";
    public const string NotARegularFile = "not_a_regular_file";
    public const string Unreadable = "unreadable";
    public const string OrphanSidecar = "orphan_sidecar";

    public const string InboxMissing = "inbox_missing";
    public const string InboxPermissions = "inbox_permissions";
    public const string InboxOutside = "inbox_outside";

    public const string ContentChanged = "content_changed";
}
