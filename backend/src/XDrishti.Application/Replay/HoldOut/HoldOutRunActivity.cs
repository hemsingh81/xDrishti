namespace XDrishti.Application.Replay.HoldOut;

/// <summary>Whether a hold-out system replay is running now (SR-03). The queued replay job (<see cref="Job.ReplayRunner"/>) does not claim a run while it is, and the hold-out loop does not start while a queued replay is in hand, so two heavy replays never share the memory budget.</summary>
public sealed class HoldOutRunActivity
{
    private volatile bool _running;

    /// <summary>True while the hold-out loop is running a claimed registration.</summary>
    public bool Running => _running;

    internal void Set(bool running) => _running = running;
}
