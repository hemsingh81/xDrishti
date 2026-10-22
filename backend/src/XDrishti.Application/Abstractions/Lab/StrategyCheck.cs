using XDrishti.Application.Abstractions.Research;

namespace XDrishti.Application.Abstractions.Lab;

/// <summary>The answer of <see cref="IStrategyChecker.Check"/>: the description (null when any problem is an error), the version the text declares (null if none) and every problem, errors and warnings, in file order.</summary>
public sealed record StrategyCheck(StrategyDescription? Description, int? Version, IReadOnlyList<StrategyIssue> Issues);
