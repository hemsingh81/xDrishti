namespace XDrishti.Domain.Learning;

/// <summary>One closed trade as the drift monitors read it: it is known from <paramref name="KnownAtEpochSeconds"/> (the exit) and not before.</summary>
/// <param name="Key">A stable id (the trade key); breaks ties in time.</param>
/// <param name="KnownAtEpochSeconds">The instant the result became known.</param>
/// <param name="R">The result in the unit of the cell.</param>
public sealed record DriftObservation(string Key, long KnownAtEpochSeconds, double R);
