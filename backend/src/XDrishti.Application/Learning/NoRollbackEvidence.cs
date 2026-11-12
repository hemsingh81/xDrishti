using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The only <see cref="IChallengerEvidence"/> of CF-10 part 10d-1: nothing is available, so promotion is refused with the reason and automatic promotion can never fire.
/// </summary>
internal sealed class NoChallengerEvidence : IChallengerEvidence
{
    // shortcut: no stored challenger scores per shadow candidate exist (CF-08 does not record the model), so the paired comparison with the champion has no source; and a registered model is not linked to the P6-12 counterfactual of its recipe (the stored report names the recipe in
    // free text that does not match the registry's parameters line). Both are later sources behind this port; neither changes the rule.
    public ValueTask<ChallengerFigures> ReadAsync(StoredModel challenger, StoredModel? champion, CancellationToken cancellationToken) => ValueTask.FromResult(ChallengerFigures.None);
}
