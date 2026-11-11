namespace XDrishti.Application.Replay;

/// <summary>The universe at a session: the members of the chosen baskets and instruments as they were at its close, and whether the earliest known membership stood in for a date before it was known.</summary>
/// <param name="Members">The members, by symbol.</param>
/// <param name="Survivorship">True when a basket's membership is not known as far back as the session (ISS-22): the earliest known membership was used.</param>
public sealed record ReplayUniverse(IReadOnlyList<ReplayInstrument> Members, bool Survivorship);
