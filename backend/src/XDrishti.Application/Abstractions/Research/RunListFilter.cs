namespace XDrishti.Application.Abstractions.Research;

/// <summary>The closed filter of the list of stored runs (spec P3-10, D7): every member is optional and equality (or a range for the instant).</summary>
/// <param name="StrategyId">The strategy id.</param>
/// <param name="EngineVersion">The engine version string.</param>
/// <param name="Costed">True: only costed runs; false: only gross runs; null: both.</param>
/// <param name="CreatedFrom">Stored at or after this instant.</param>
/// <param name="CreatedTo">Stored before this instant.</param>
public sealed record RunListFilter(string? StrategyId = null, string? EngineVersion = null, bool? Costed = null, DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null);
