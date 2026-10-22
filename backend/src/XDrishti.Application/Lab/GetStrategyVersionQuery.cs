using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>One version of a family (spec P5-01); <c>lab.not_found</c> when the family or the version is not stored.</summary>
public sealed record GetStrategyVersionQuery(string Id, int Version) : IQuery<Result<StrategyVersionView>>;
