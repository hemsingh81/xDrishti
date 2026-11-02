namespace XDrishti.Domain.Learning;

/// <summary>The extension group that was appended to the rows of a <see cref="TrainingData"/> (P13-05): its version, its names in column order (after the catalog values) and what was dropped for it.</summary>
/// <param name="Version">The extension version, for example <c>md-1</c>.</param>
/// <param name="Names">The extension feature names in column order.</param>
/// <param name="DroppedNoExtension">Labelled trades with a catalog row but no extension row.</param>
/// <param name="UnknownByName">For each required extension name, how many trades had it unknown.</param>
public sealed record TrainingDataExtension(string Version, IReadOnlyList<string> Names, int DroppedNoExtension, IReadOnlyDictionary<string, int> UnknownByName);
