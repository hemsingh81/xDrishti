namespace XDrishti.Application.Reporting;

/// <summary>
/// Lets one report at a time hold a population in memory (P8-06, ADR 0100). A report reads every closed trade of its runs into memory; a page that fires several reports at once on a large run would hold several populations together and could take the container past its memory
/// limit. A request that cannot enter waits, and gives up when its token is cancelled. Process-wide (a singleton): the loader is scoped, the memory is not.
/// </summary>
internal sealed class ReportGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Waits for the gate. The lease releases it when disposed.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Lease(_gate);
    }

    public void Dispose() => _gate.Dispose();

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
