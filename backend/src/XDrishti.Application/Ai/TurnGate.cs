namespace XDrishti.Application.Ai;

/// <summary>One turn at a time (spec P9-03 NFR): a second request is refused at once with <c>assistant.busy</c>, never queued. A singleton.</summary>
public sealed class TurnGate : IDisposable
{
    private readonly SemaphoreSlim _slot = new(1, 1);

    /// <summary>Takes the slot if it is free; the returned object gives it back when disposed (once). Null when a turn is already running.</summary>
    public IDisposable? TryEnter() => _slot.Wait(0) ? new Releaser(_slot) : null;

    /// <summary>Whether a turn holds the slot now.</summary>
    public bool Busy => _slot.CurrentCount == 0;

    public void Dispose() => _slot.Dispose();

    private sealed class Releaser(SemaphoreSlim slot) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                slot.Release();
            }
        }
    }
}
