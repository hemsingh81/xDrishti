namespace XDrishti.DhanTrial;

internal interface IRateLimiter
{
    Task WaitAsync(CancellationToken cancellationToken);
}

/// <summary>Spaces calls at least <paramref name="interval"/> apart (5 requests/second for Dhan Data APIs = 200 ms).</summary>
internal sealed class MinIntervalRateLimiter(TimeSpan interval, TimeProvider clock) : IRateLimiter, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _next = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            if (now < _next)
            {
                await Task.Delay(_next - now, clock, cancellationToken);
            }

            _next = clock.GetUtcNow() + interval;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

/// <summary>No spacing — used for the deliberate burst test that probes the server-side limit.</summary>
internal sealed class NoRateLimiter : IRateLimiter
{
    public Task WaitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
