namespace XDrishti.Execution.Application.Readiness;

/// <summary>The owner's "check now" (command <c>readiness_now</c>): sets a flag and wakes the loop, which probes at once. Holds no data about the request.</summary>
public sealed class ReadinessSignal : IDisposable
{
    private readonly SemaphoreSlim _wake = new(0, 1);
    private int _requested;

    public void Request()
    {
        Interlocked.Exchange(ref _requested, 1);
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // already signalled
        }
    }

    /// <summary>Waits up to <paramref name="timeout"/> for a request.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _wake.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _wake.Dispose();

    /// <summary>True once per request.</summary>
    public bool Consume() => Interlocked.Exchange(ref _requested, 0) == 1;
}
