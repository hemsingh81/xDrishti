using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>One changelog row (P6-08).</summary>
/// <param name="Id">The row id.</param>
public sealed record GetChangelogEntryQuery(long Id) : IQuery<Result<ChangelogEntryView>>;
