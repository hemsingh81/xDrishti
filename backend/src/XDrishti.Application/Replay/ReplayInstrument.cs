namespace XDrishti.Application.Replay;

/// <summary>One instrument of the replay universe.</summary>
/// <param name="Id">The instrument id.</param>
/// <param name="Symbol">The symbol.</param>
public sealed record ReplayInstrument(Guid Id, string Symbol);
