using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Research;

/// <summary>A page of stored runs, newest first (spec P3-10, D7).</summary>
public sealed record ListStoredRunsQuery(RunListFilter Filter, PageRequest Page) : IQuery<Result<Page<StoredRunHeader>>>;
