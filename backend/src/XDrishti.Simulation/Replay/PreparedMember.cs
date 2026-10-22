using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Replay;

/// <summary>One candidate of a prepared strategy (P4-05): its id, its exit (null for a plain run with the built-in's own exit), the record its run keeps and the hash of its definition without exits and overlays (the entries part of the comparison key).</summary>
/// <param name="Id">The id of the candidate.</param>
/// <param name="Exit">The exit, or null.</param>
/// <param name="Record">The strategy as the run of this candidate records it.</param>
/// <param name="EntriesDefinitionSha256">SHA-256 of the canonical definition with the exits and overlays removed.</param>
public sealed record PreparedMember(string Id, ExitCandidate? Exit, StrategyRecord Record, string EntriesDefinitionSha256);
