namespace XDrishti.SimExport.Manifest;

/// <summary>A manifest read back (spec P3-04b1, E6), checked for structure and for its own export key: what the folder writer compares with a new export to decide that it is unchanged.</summary>
/// <param name="Format">The format string the exporter was given (<c>xd-sim-export-1</c>).</param>
/// <param name="RunKey">The run key (64 lower-case hex characters).</param>
/// <param name="BasketId">The basket of the universe, or null.</param>
/// <param name="ExportKey">The export key the manifest states; it equals <see cref="ExportKey.Of"/> of the other fields.</param>
/// <param name="Entries">The files listed, in the order of the manifest (ordinal name order).</param>
/// <param name="ExtraLines">The extra lines, in order.</param>
public sealed record ParsedManifest(string Format, string RunKey, Guid? BasketId, string ExportKey, IReadOnlyList<ManifestEntry> Entries, IReadOnlyList<string> ExtraLines);
