namespace XDrishti.Application.Abstractions.Learning;

/// <summary>
/// The owner's switch <c>ml.influence.enabled</c> (spec CF-10 D6). Off when nothing says it is on. Its one implementation reads the newest row of <c>research.ml_switch_settings</c> for that name (CF-10 part 10d-1, ADR 0220): no row means OFF, and so does a row that is OFF.
/// Only the owner's act (a fresh password) appends an ON row. Even ON, it opens nothing alone: the predicate's other terms must hold too.
/// </summary>
public interface IMlInfluenceSwitch
{
    /// <summary>Whether the owner has switched model influence on; false by default and whenever it cannot be read.</summary>
    ValueTask<bool> IsOnAsync(CancellationToken cancellationToken);
}
