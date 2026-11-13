namespace XDrishti.Application.Replay.HoldOut;

/// <summary>
/// The one gate between the queued replay job (<see cref="Job.ReplayRunner"/>) and the hold-out system replay (SR-03), so two heavy replays never share the memory budget. It is atomic: idle, held by the hold-out loop, or held by the replay job, and each side takes it
/// with a compare-and-exchange before it claims anything. The replay job holds it for as long as it has a run in hand; the hold-out loop holds it for one pass.
/// </summary>
public sealed class HoldOutRunActivity
{
    private const int Idle = 0;
    private const int HoldOut = 1;
    private const int Replay = 2;

    private int _state;

    /// <summary>True while the hold-out loop holds the gate.</summary>
    public bool Running => Volatile.Read(ref _state) == HoldOut;

    internal bool TryEnterHoldOut() => Interlocked.CompareExchange(ref _state, HoldOut, Idle) == Idle;

    internal void ExitHoldOut() => Interlocked.CompareExchange(ref _state, Idle, HoldOut);

    internal bool TryEnterReplay() => Interlocked.CompareExchange(ref _state, Replay, Idle) == Idle;

    internal void ExitReplay() => Interlocked.CompareExchange(ref _state, Idle, Replay);
}
