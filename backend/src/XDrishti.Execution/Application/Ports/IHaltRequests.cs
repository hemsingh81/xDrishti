namespace XDrishti.Execution.Application.Ports;

/// <summary>The halt levels of P12-05, mildest first. A request names a level; P12-05 decides.</summary>
public enum HaltLevel
{
    Entries,
    Cancel,
    ExitAll,
    Freeze,
}

/// <summary>
/// The call-out to the kill switch and halts (P12-05 implements it). A request can only ask for MORE caution; nothing here lifts a halt. Until P12-05 only <see cref="RecordingHaltRequests"/> exists, and no host registers it.
/// </summary>
public interface IHaltRequests
{
    Task RequestAsync(Guid? accountId, HaltLevel level, string triggerCode, string detail, CancellationToken cancellationToken);
}

/// <summary>Remembers the requests it was given (tests and the CI rollout step). It halts nothing.</summary>
public sealed class RecordingHaltRequests : IHaltRequests
{
    private readonly Lock _gate = new();
    private readonly List<(Guid? AccountId, HaltLevel Level, string TriggerCode)> _requests = [];

    public IReadOnlyList<(Guid? AccountId, HaltLevel Level, string TriggerCode)> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public Task RequestAsync(Guid? accountId, HaltLevel level, string triggerCode, string detail, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _requests.Add((accountId, level, triggerCode));
        }

        return Task.CompletedTask;
    }
}
