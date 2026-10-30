namespace XDrishti.Execution.Application.Recon;

/// <summary>The broker read budget (spec P12-06 criterion 11): on average at most three reads a second over a ten second window, kept apart from the send limiter so a busy poll never starves a send. A read that does not fit is not made; its owner waits for the next tick.</summary>
public sealed class ReadBudget(TimeProvider clock, int perSecond = 3)
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly List<DateTimeOffset> _reads = [];

    public bool TryAdmit(int reads)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            _reads.RemoveAll(t => now - t >= Window);
            if (_reads.Count + reads > perSecond * (int)Window.TotalSeconds)
            {
                return false;
            }

            for (var i = 0; i < reads; i++)
            {
                _reads.Add(now);
            }

            return true;
        }
    }
}
