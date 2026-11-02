using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>An announcement row ready to be stored (P15-07): its envelope (kind <c>announcement</c>, no period, the row's broadcast time) and the category and subject kept in <c>filings.announcements</c>. The text lives in the stored file of the envelope.</summary>
public sealed record NewAnnouncement(NewFiling Filing, string Category, string Headline);

/// <summary><c>filings.filings</c> plus <c>filings.announcements</c> (P15-07): one row is one transaction, insert-only; every announcement is its own supersede group (no period). Idempotent on the content hash.</summary>
public interface IAnnouncementStore
{
    ValueTask<FilingInsertResult> InsertAsync(NewAnnouncement announcement, CancellationToken cancellationToken);
}
