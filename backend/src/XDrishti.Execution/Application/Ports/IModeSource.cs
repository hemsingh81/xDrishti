using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Ports;

/// <summary>The effective mode of an account. Until P12-07 the only implementation answers nothing, so every gate is closed.</summary>
public interface IModeSource
{
    Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken);
}

internal sealed class ClosedModeSource : IModeSource
{
    public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult<ModeAnswer?>(null);
}
