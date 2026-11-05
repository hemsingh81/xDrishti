namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The owner's switch <c>ml.influence.enabled</c> (spec CF-10 D6). Off when nothing says it is on. Its stored form and the owner act that changes it (a fresh password) are part 10d; until then the only implementation answers off.</summary>
public interface IMlInfluenceSwitch
{
    /// <summary>Whether the owner has switched model influence on; false by default and whenever it cannot be read.</summary>
    ValueTask<bool> IsOnAsync(CancellationToken cancellationToken);
}
