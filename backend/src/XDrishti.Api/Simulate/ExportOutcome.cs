using XDrishti.SimExport.Manifest;

namespace XDrishti.Api.Simulate;

/// <summary>What a successful write reports.</summary>
/// <param name="Status">Written, unchanged or replaced.</param>
/// <param name="Folder">The run folder.</param>
/// <param name="ExportKey">The export key of the files that are in the folder.</param>
/// <param name="Entries">The files, as the manifest lists them (the manifest itself is not one).</param>
/// <param name="StaleStaging">The names of <c>.staging-*</c> folders left by a process that was killed; never removed by the writer: the owner removes them.</param>
/// <param name="PreviousFolder">The folder the old export was renamed to, for <see cref="ExportStatus.ReplacedPrevious"/>; otherwise null.</param>
internal sealed record ExportOutcome(ExportStatus Status, string Folder, string ExportKey, IReadOnlyList<ManifestEntry> Entries, IReadOnlyList<string> StaleStaging, string? PreviousFolder);
