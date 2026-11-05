using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The only <see cref="IMlInfluenceEvidence"/> of CF-10 part 10c-2: nothing is available, so no model can pass.
/// </summary>
internal sealed class NoMlInfluenceEvidence : IMlInfluenceEvidence
{
    // shortcut: no step scores a model on the hold-out rows (rows, probability against the no-ML baseline, effect of the take rule); a shadow candidate does not record which model scored it (CF-08), so the forward evidence cannot be tied to a model; the P6-09 calibration detector
    // is replayed on demand over named runs and not stored per model (the daily ml-monitor job is part 10d). Each is a later source behind this port; none changes the predicate.
    public ValueTask<MlInfluenceEvidence> ReadAsync(StoredModel model, CancellationToken cancellationToken) => ValueTask.FromResult(MlInfluenceEvidence.None);
}
