namespace XDrishti.Domain.Research;

/// <summary>A simulated trade of a run with the baskets of the universe it came from (none for an explicit list of instruments, one for a basket; doc 10 §1: every candidate carries its basket). <see cref="SimTrade"/> is not changed by it.</summary>
public sealed record RunTrade(SimTrade Trade, IReadOnlyList<Guid> BasketIds);
