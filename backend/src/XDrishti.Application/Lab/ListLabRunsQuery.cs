using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>Lab runs newest first (spec P5-02), optionally of one strategy and one state (a state word); at most <paramref name="Limit"/> (default 50, largest 200).</summary>
public sealed record ListLabRunsQuery(string? StrategyId, string? State, int? Limit) : IQuery<Result<IReadOnlyList<LabRunView>>>;
