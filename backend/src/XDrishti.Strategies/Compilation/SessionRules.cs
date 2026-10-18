namespace XDrishti.Strategies.Compilation;

/// <summary>The time rules as minutes of the India day (09:30 is 570), or sessions: what the simulator and <see cref="BoundSide.InEntryWindow"/> use. A rule the strategy does not have is null.</summary>
public readonly record struct SessionRules(int? FirstEntryMinute, int? LastEntryMinute, int? SquareOffMinute, int? MaxHoldSessions);
