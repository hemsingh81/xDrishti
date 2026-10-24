using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Builds the weekly learning digest from stored records and stores it once per ISO week (P7-15, ADR 0094). It reads; the only thing it writes is the digest. It applies nothing, approves nothing and places no order.
/// </summary>
/// <param name="Week">The ISO week (<c>2026-W43</c>); null for the week of today (India time).</param>
/// <param name="Store">Store the digest (only when the week may be stored, see <c>DigestWeek.StoreRefusal</c>); false builds and shows it and stores nothing.</param>
/// <param name="Actor">Who asked (<c>system:weekly</c>, <c>cli:xd</c>).</param>
public sealed record BuildDigestCommand(string? Week, bool Store, string Actor) : ICommand;

/// <summary>The weekly digest as the API and the verb show it.</summary>
/// <param name="Id">The row id; null for a dry run that stored nothing.</param>
/// <param name="IsoWeek">The week.</param>
/// <param name="WeekStart">The Monday.</param>
/// <param name="WeekEnd">The Sunday.</param>
/// <param name="Sections">The sections.</param>
/// <param name="Text">The text.</param>
/// <param name="BuiltBy">Who built it.</param>
/// <param name="CreatedAt">When it was stored; null for a dry run.</param>
/// <param name="Repeated">The week already had a digest: that one is shown and nothing was written.</param>
public sealed record DigestView(long? Id, string IsoWeek, DateOnly WeekStart, DateOnly WeekEnd, IReadOnlyList<DigestSection> Sections, string Text, string? BuiltBy, DateTimeOffset? CreatedAt, bool Repeated);

/// <summary>One stored digest: of a week, or the newest when <paramref name="Week"/> is null (P7-15). Read only.</summary>
/// <param name="Week">The ISO week, or null for the newest.</param>
public sealed record GetDigestQuery(string? Week) : IQuery<Result<DigestView>>;

/// <summary>The stored digests, newest week first (P7-15). Read only.</summary>
/// <param name="Limit">1 to 52.</param>
public sealed record ListDigestsQuery(int Limit) : IQuery<Result<IReadOnlyList<DigestView>>>;
