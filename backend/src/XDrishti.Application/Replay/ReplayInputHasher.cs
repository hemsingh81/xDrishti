using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay;

/// <summary>
/// Takes the input hash of a run (SR-01 part B): the parameters, the settings snapshot, the scope, the members at every session of the range, the frozen statistics snapshot, the strategy versions (all that the run may use) and the bars. The start stores it; the job takes it again at every claim and
/// fails a run whose inputs have changed since the start (<c>replay.diverged</c>), so a resumed run is never mixed from two states of the data.
/// </summary>
internal sealed class ReplayInputHasher(IReplayFacts facts, IReplayInputs inputs)
{
    /// <summary>The input hash and the scope hash of a run.</summary>
    public async ValueTask<(string Input, string Scope)> ComputeAsync(ReplayParams parameters, ReplaySettingsSnapshot settings, IReadOnlyDictionary<string, IReadOnlyList<StoredVersion>> versions, IReadOnlyList<DateOnly> sessions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var perSession = new List<(DateOnly, IReadOnlyList<Guid>)>();
        var union = new HashSet<Guid>(parameters.Scope.InstrumentIds);
        foreach (var session in sessions)
        {
            var members = (await facts.UniverseAsync(parameters, session, cancellationToken)).Members.Select(m => m.Id).Order().ToList();
            perSession.Add((session, members));
            union.UnionWith(members);
        }

        var all = union.Order().ToList();
        var scope = ReplayHashes.Scope(parameters.Scope, all);
        var keys = (await facts.CellsAsync(parameters, parameters.From, cancellationToken)).Cells.Select(c => c.Stored.SnapshotKey).Distinct(StringComparer.Ordinal).ToList();
        var bars = await inputs.BarHashAsync(all, parameters.From, parameters.To, cancellationToken);
        var hashes = versions.ToDictionary(v => v.Key, v => string.Join(',', v.Value.OrderBy(x => x.Version).Select(x => x.Version + ":" + x.Hash)), StringComparer.Ordinal);
        return (ReplayHashes.Input(parameters, settings, scope, ReplayHashes.Membership(perSession), keys, hashes, bars), scope);
    }
}
