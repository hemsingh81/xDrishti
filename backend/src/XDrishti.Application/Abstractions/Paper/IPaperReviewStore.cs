using XDrishti.Domain.Paper;

namespace XDrishti.Application.Abstractions.Paper;

/// <summary>A review to store.</summary>
/// <param name="Week">The ISO week.</param>
/// <param name="EpochId">The epoch.</param>
/// <param name="BuiltAt">When it was built.</param>
/// <param name="BuiltBy">Who built it.</param>
/// <param name="Text">The text; the database refuses one that does not open with the pinned line.</param>
/// <param name="Sections">The sections.</param>
public sealed record NewPaperReview(string Week, long EpochId, DateTimeOffset BuiltAt, string BuiltBy, string Text, IReadOnlyList<ReviewSection> Sections);

/// <summary>A stored review.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Review">What was stored.</param>
public sealed record StoredPaperReview(long Id, NewPaperReview Review);

/// <summary>
/// The stored weekly paper reviews (<c>paper.weekly_reviews</c>, spec P10-03, ADR 0121), insert-only: one per ISO week and epoch, the first kept. No member updates or deletes.
/// </summary>
public interface IPaperReviewStore
{
    /// <summary>Stores the review unless that week and epoch has one; returns the stored one and whether this call wrote it.</summary>
    ValueTask<(StoredPaperReview Stored, bool Created)> InsertAsync(NewPaperReview review, CancellationToken cancellationToken);

    /// <summary>The review of a week and epoch, or null.</summary>
    ValueTask<StoredPaperReview?> FindAsync(string isoWeek, long epochId, CancellationToken cancellationToken);

    /// <summary>The stored reviews (of one epoch when given), newest week first.</summary>
    ValueTask<IReadOnlyList<StoredPaperReview>> ListAsync(long? epochId, int limit, CancellationToken cancellationToken);
}
