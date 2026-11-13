using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Shadow;

/// <summary>
/// The evidence set of the current edge epoch (spec AM-01 GAP-09, ADR 0282): the stored set with its state, or, while none is stored, the versions the default rule would register at the epoch's first shadow write. Read only: it stores nothing and writes nothing.
/// </summary>
internal sealed class GetEvidenceSetHandler(IEvidenceSetStore sets, IPaperStore paper, IConfigSnapshotSource configuration, IShadowSettings settings) : IQueryHandler<GetEvidenceSetQuery, EvidenceSetView>
{
    public async ValueTask<EvidenceSetView> HandleAsync(GetEvidenceSetQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var epoch = (await paper.CurrentEpochAsync(cancellationToken))?.Id;
        var enabled = await settings.IsEnabledAsync(cancellationToken);
        if (await sets.CurrentAsync(EvidenceSetRules.EpochKey(epoch), cancellationToken) is { } stored)
        {
            var note = stored.Mode == EvidenceSetRules.ModeBackfill
                ? "Frozen, written by the migration that introduced evidence sets from the versions of the rows already stored: a backfill, NOT a pre-registration (the versions were not chosen before their results). It can never be replaced."
                : stored.Frozen
                ? "Frozen: a shadow row of this epoch is stored, so the set can never be replaced. Rows of any other version are still written and graded, labelled outside_set, and left out of the edge count and the readiness forward rows."
                : "Open: the owner may replace the set until the first shadow write of this epoch; after that it is frozen.";
            return new EvidenceSetView(epoch, stored.Frozen ? "frozen" : "open", stored.Versions, stored.SetHash, stored.Mode, stored.Actor, stored.Reason, stored.CreatedAt, stored.FirstWriteAt, enabled, note);
        }

        var preview = await EvidenceSetDefault.ReadAsync(configuration, cancellationToken) ?? [];
        var pending = preview.Count == 0
            ? "No set is stored and no strategy version can be named from the configuration reading (no eligible cell, or the cells could not be read): the shadow record writes nothing and counts nothing until a set exists. The owner may name a set."
            : "No set is stored yet: the first shadow write of this epoch registers the versions of the eligible cells as the set (automatic, before the entries, in the same transaction). The owner may replace it until then.";
        return new EvidenceSetView(epoch, "pending", preview, null, null, null, null, null, null, enabled, pending);
    }
}
