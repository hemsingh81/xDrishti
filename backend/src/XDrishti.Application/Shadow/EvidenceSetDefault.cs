using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Shadow;

/// <summary>
/// The default evidence set, read fresh (spec AM-01 GAP-09, ADR 0282): the strategy versions of the eligible cells as the configuration reading finds them now, not from a stored snapshot (a stored full snapshot exists only once a paper account has started a capture, which the
/// shadow default must not wait for). A reading in which the cells group could not be read is partial and names nothing: the shadow record then writes nothing (fail closed).
/// </summary>
internal static class EvidenceSetDefault
{
    public const string CellsGroup = "cells";

    public static async ValueTask<IReadOnlyList<string>?> ReadAsync(IConfigSnapshotSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var reading = await source.ReadAsync(cancellationToken);
        return reading.Unreadable.Contains(CellsGroup, StringComparer.Ordinal) ? null : EvidenceSetRules.Default(reading.Components);
    }
}
