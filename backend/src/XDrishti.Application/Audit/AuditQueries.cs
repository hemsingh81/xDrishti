using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Audit;

/// <summary>The filters of the list and of the summary; every text is as received (comma lists), validated by the handler.</summary>
public sealed record AuditFilters(string? From, string? To, string? Source, string? Category, string? Kind, string? Outcome, string? Actor, string? Account, string? Text);

/// <summary>A page of the merged audit (spec P8-22).</summary>
public sealed record ListAuditQuery(AuditFilters Filters, int? Limit, string? Cursor, string? OwnerEmail) : IQuery<Result<AuditPageView>>;

/// <summary>One row with its detail.</summary>
public sealed record GetAuditEntryQuery(string Id, string? OwnerEmail) : IQuery<Result<AuditEntryDetailView>>;

/// <summary>Counts of the window.</summary>
public sealed record GetAuditSummaryQuery(string? From, string? To) : IQuery<Result<AuditSummaryView>>;

/// <summary>The kinds each store can hold.</summary>
public sealed record ListAuditKindsQuery : IQuery<AuditKindsView>;
