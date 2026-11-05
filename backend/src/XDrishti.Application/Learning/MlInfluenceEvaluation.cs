using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// One evaluation of the predicate for a registered model, with the figures it read and the composed answer (CF-10 part 10c-2): the four terms of the predicate plus <see cref="IsChampion"/>. The gate, the stored check and the view all read these members, so they cannot differ.
/// </summary>
/// <param name="Model">The model.</param>
/// <param name="HoldOut">Where it stands with the hold-out.</param>
/// <param name="Facts">What the predicate read.</param>
/// <param name="Result">The predicate's answer (four terms).</param>
/// <param name="SettingHash">SHA-256 of the thresholds and the switch.</param>
internal sealed record MlInfluenceEvaluation(StoredModel Model, HoldOutReading HoldOut, MlInfluenceFacts Facts, MlInfluenceResult Result, string SettingHash)
{
    /// <summary>The extra term the gate adds: the model must be the current champion.</summary>
    public const string IsChampion = nameof(IsChampion);

    /// <summary>Whether the model is the current champion.</summary>
    public bool Champion => Model.Role == ModelRole.Champion;

    /// <summary>The four terms of the predicate and <see cref="IsChampion"/>.</summary>
    public IReadOnlyList<MlTermResult> Terms =>
    [
        .. Result.Terms,
        Champion
            ? new MlTermResult(IsChampion, MlTermState.Holds, [])
            : new MlTermResult(IsChampion, MlTermState.Fails, [new MlInfluenceReason("not_champion", "The model is not the current champion; only the champion's score is ever handed out.")]),
    ];

    /// <summary>True only when the predicate holds and the model is the champion.</summary>
    public bool MayInfluence => Result.MayInfluence && Champion;

    /// <summary>Every reason of every term that does not hold; empty only when <see cref="MayInfluence"/>.</summary>
    public IReadOnlyList<MlInfluenceReason> Reasons => MayInfluence ? [] : [.. Terms.SelectMany(t => t.Reasons)];
}
