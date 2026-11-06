using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The only <see cref="IRollbackEvidence"/> of CF-10 part 10d-1: nothing is available, so the rollback triggers read "unavailable" and fire nothing.
/// </summary>
internal sealed class NoRollbackEvidence : IRollbackEvidence
{
    // shortcut: a shadow candidate does not record which model scored it (CF-08), so the champion's scored outcomes (probability and label) and the trade series of the cells it scores have no source. The detectors (P6-09) are real and tested with fakes; a later slice that stores the
    // model id and score per shadow candidate adds the second implementation here and replaces this one.
    public ValueTask<RollbackInputs> ReadAsync(StoredModel champion, CancellationToken cancellationToken) => ValueTask.FromResult(RollbackInputs.None);
}

/// <summary>
/// The only <see cref="IChallengerEvidence"/> of CF-10 part 10d-1: nothing is available, so promotion is refused with the reason and automatic promotion can never fire.
/// </summary>
internal sealed class NoChallengerEvidence : IChallengerEvidence
{
    // shortcut: no stored challenger scores per shadow candidate exist (CF-08 does not record the model), so the paired comparison with the champion has no source; and a registered model is not linked to the P6-12 counterfactual of its recipe (the stored report names the recipe in
    // free text that does not match the registry's parameters line). Both are later sources behind this port; neither changes the rule.
    public ValueTask<ChallengerFigures> ReadAsync(StoredModel challenger, StoredModel? champion, CancellationToken cancellationToken) => ValueTask.FromResult(ChallengerFigures.None);
}
