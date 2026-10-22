using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>One family and its versions (spec P5-01); <c>lab.not_found</c> when it is not stored.</summary>
public sealed record GetStrategyQuery(string Id) : IQuery<Result<StrategyDetail>>;
