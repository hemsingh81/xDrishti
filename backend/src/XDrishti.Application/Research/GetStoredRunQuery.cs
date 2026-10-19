using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Research;

/// <summary>The header of a stored run by its run key (spec P3-10, D7); <c>store.run_not_found</c> when none is stored.</summary>
public sealed record GetStoredRunQuery(string RunKey) : IQuery<Result<StoredRunHeader>>;
