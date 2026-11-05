namespace XDrishti.Application.Abstractions.Learning;

/// <summary><c>research.ml_influence_checks</c> behind a port (CF-10 part 10c-2): the insert-only record of how the influence predicate stood for a model.</summary>
public interface IMlInfluenceChecks
{
    /// <summary>Appends the check unless the newest check of the model has the same <see cref="MlInfluenceCheck.InputsHash"/> (nothing is written then: false).</summary>
    ValueTask<bool> AppendIfChangedAsync(MlInfluenceCheck check, CancellationToken cancellationToken);
}
