using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Replay;

/// <summary>One cell as the replay sees it at a session: its statistics row, its state, and the strategy version the rule gives (null: <c>no_version</c>).</summary>
/// <param name="Stored">The statistics row with its snapshot facts.</param>
/// <param name="State">The state as of the cut; null when not known.</param>
/// <param name="Version">The strategy version under the version rule; null when none.</param>
public sealed record ReplayCell(StoredCellStats Stored, CellState? State, StoredVersion? Version);
