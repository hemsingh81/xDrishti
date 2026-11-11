namespace XDrishti.Application.Replay;

/// <summary>A read of market data past the replay's clamp (the hold-out start less the embargo) was asked for. This is a programming or wiring error, never an expected outcome: it stops the run instead of reading the hold-out (SR-01, AC 3).</summary>
public sealed class ReplayHoldOutException : InvalidOperationException
{
    /// <summary>A refusal with the date asked and the last readable date.</summary>
    public ReplayHoldOutException(DateOnly asked, DateOnly lastReadable)
        : base($"A read of {asked:yyyy-MM-dd} was asked for; the replay may read up to {lastReadable:yyyy-MM-dd} (the hold-out start less its embargo).")
    {
    }

    /// <summary>A refusal when no hold-out block is fixed: the replay fails closed.</summary>
    public ReplayHoldOutException()
        : base("No hold-out block is fixed, so the replay cannot know how far it may read.")
    {
    }
}
