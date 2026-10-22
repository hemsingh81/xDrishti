namespace XDrishti.Simulation.Replay;

/// <summary>What <see cref="StoredStrategies.Check"/> returns: every problem (errors and warnings) in file order, the version the text declares (null if none) and the description when no problem is an error.</summary>
public sealed record StoredCheck(StoredDescription? Description, int? Version, IReadOnlyList<StoredIssue> Issues);
