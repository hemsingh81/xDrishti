namespace XDrishti.Domain.Learning;

/// <summary>One scored decision whose label is known: the probability a model gave, and what happened. Known from <paramref name="KnownAtEpochSeconds"/> (the end of the label horizon) and not before.</summary>
/// <param name="Key">A stable id (the trade key); breaks ties in time.</param>
/// <param name="KnownAtEpochSeconds">The instant the label became known.</param>
/// <param name="Predicted">The model's probability, in [0, 1].</param>
/// <param name="Label">0 or 1.</param>
public sealed record CalibrationObservation(string Key, long KnownAtEpochSeconds, double Predicted, int Label);
