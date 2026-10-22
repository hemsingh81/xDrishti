namespace XDrishti.Domain.Research;

/// <summary>The winner of the plateau rule (P4-06, D7) and how it won.</summary>
/// <param name="Id">The candidate id.</param>
/// <param name="OnPlateau">Every qualified neighbour scores within one standard error of it or better, and it scores within one standard error of the best.</param>
/// <param name="Isolated">It has no qualified neighbour.</param>
/// <param name="LonePeak">No candidate was eligible; the highest score won.</param>
public sealed record PlateauChoice(string Id, bool OnPlateau, bool Isolated, bool LonePeak);
