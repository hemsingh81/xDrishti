using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The only <see cref="IMlInfluenceSwitch"/> of CF-10 part 10c-2: always off. The stored switch and the owner act that turns it on (a fresh password, an append-only setting history) are part 10d, so until then no model can be allowed to influence a decision.
/// </summary>
internal sealed class MlInfluenceSwitchOff : IMlInfluenceSwitch
{
    public ValueTask<bool> IsOnAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);
}
