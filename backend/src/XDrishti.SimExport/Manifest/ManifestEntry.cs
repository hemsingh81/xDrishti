namespace XDrishti.SimExport.Manifest;

/// <summary>
/// One file of an export as the manifest lists it (spec P3-04b1, E6): its name relative to the run folder (at most one <c>/</c>, so <c>cards/0001-x.html</c> fits), its length in bytes and its lower-case hex SHA-256. The library works over a
/// list of these plus a list of extra lines and does not know which files exist, so a later slice adds files without changing a type here.
/// </summary>
public sealed record ManifestEntry(string Name, long Bytes, string Sha256);
