namespace XDrishti.Domain.Market.Context;

/// <summary>One tracked share and its closes by session: only the sessions on which it has a complete <c>1D</c> bar are in <paramref name="Closes"/>; a session without a bar is absent, never filled from another day.</summary>
public sealed record BreadthInput(Guid ShareId, IReadOnlyDictionary<DateOnly, double> Closes);
