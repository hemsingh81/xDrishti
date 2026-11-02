namespace XDrishti.Application.Filings;

/// <summary>What a verb of <c>xd eval announcements</c> did, as lines to print (counts, ids and hashes only, never an announcement's text). <see cref="Changed"/> is false when the files were already as the verb would write them.</summary>
public sealed record AnnouncementEvalReport(bool Changed, IReadOnlyList<string> Lines);
