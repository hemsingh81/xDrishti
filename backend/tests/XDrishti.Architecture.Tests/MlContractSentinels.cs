using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Architecture.Tests;

// The sentinels of the ML architecture tests that implement a contract of production. They are top-level types and not nested in their test classes: the closure scan of KillArchitectureTests
// follows a contract that is named to every class that implements it, and a nested type to its declaring type. Since CF-11 the automatic path reaches the readiness gate and through it the phase
// check and the schedule reader, which names every scheduled service, hence the ML monitor and the ML contracts; a nested sentinel then pulled the whole test class (and so the registration, and the
// re-enable) into the automatic closure. A top-level sentinel names nothing but its contract. The tests that use them are unchanged.

/// <summary>Stands for a second switch of model influence.</summary>
internal sealed class SwitchSentinel : IMlInfluenceSwitch
{
    public ValueTask<bool> IsOnAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);
}

/// <summary>Stands for a second source of evidence.</summary>
internal sealed class MlInfluenceEvidenceSentinel : IMlInfluenceEvidence
{
    public ValueTask<MlInfluenceEvidence> ReadAsync(StoredModel model, CancellationToken cancellationToken) => ValueTask.FromResult(MlInfluenceEvidence.None);
}

/// <summary>Stands for the first real source of challenger evidence.</summary>
internal sealed class ChallengerEvidenceSentinel : IChallengerEvidence
{
    public ValueTask<ChallengerFigures> ReadAsync(StoredModel challenger, StoredModel? champion, CancellationToken cancellationToken) => ValueTask.FromResult(ChallengerFigures.None);
}

/// <summary>Stands for the first real source of the predicate's evidence.</summary>
internal sealed class InfluenceEvidenceSentinel : IMlInfluenceEvidence
{
    public ValueTask<MlInfluenceEvidence> ReadAsync(StoredModel model, CancellationToken cancellationToken) => ValueTask.FromResult(MlInfluenceEvidence.None);
}
