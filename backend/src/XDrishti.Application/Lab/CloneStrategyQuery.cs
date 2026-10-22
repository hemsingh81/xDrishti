using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>
/// The text of a stored version as the start of a new one (spec P5-01): stores nothing. <paramref name="NewId"/> null keeps the id for an ordinary family (the clone becomes its next version) and gives a built-in the id <c>&lt;ID&gt;_COPY</c>.
/// </summary>
public sealed record CloneStrategyQuery(string Id, int Version, string? NewId = null) : IQuery<Result<ClonedStrategy>>;
