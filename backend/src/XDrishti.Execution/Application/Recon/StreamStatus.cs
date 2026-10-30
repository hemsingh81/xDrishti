namespace XDrishti.Execution.Application.Recon;

/// <summary>What the stream endpoint shows (spec P12-06 criterion 12).</summary>
public sealed record StreamStatusView(bool Connected, DateTimeOffset? LastMessageAt, DateTimeOffset? LastPollAt, int Reconnects24h, long Dropped, long Applied, long Ignored, bool StreamsAlive);

/// <summary>
/// The state of the two update paths. <see cref="StreamsAlive"/> is true only while the last successful poll is at most 15 s old AND no gap fill is owed: after a reconnect (and at the start) one poll must complete before it is true again. The stream alone never makes it
/// true (silence is not an error; the poll decides). Time comes from the provider, so tests drive it.
/// </summary>
public sealed class StreamStatus(TimeProvider clock, UpdateCounters counters)
{
    public static readonly TimeSpan PollFreshness = TimeSpan.FromSeconds(15);

    private readonly Lock _gate = new();
    private readonly List<DateTimeOffset> _reconnects = [];
    private bool _connected;
    private bool _gapOwed = true;
    private bool _pollRequested;
    private DateTimeOffset? _lastMessageAt;
    private DateTimeOffset? _lastPollAt;

    public DateTimeOffset? LastPollAt
    {
        get
        {
            lock (_gate)
            {
                return _lastPollAt;
            }
        }
    }

    public bool GapOwed
    {
        get
        {
            lock (_gate)
            {
                return _gapOwed;
            }
        }
    }

    public void Connected()
    {
        lock (_gate)
        {
            _connected = true;
            _gapOwed = true;
            _reconnects.Add(clock.GetUtcNow());
        }
    }

    public void Disconnected()
    {
        lock (_gate)
        {
            _connected = false;
        }
    }

    public void Message()
    {
        lock (_gate)
        {
            _lastMessageAt = clock.GetUtcNow();
        }
    }

    /// <summary>A poll finished and its answer was read. It also pays a gap fill that was owed.</summary>
    public void PollDone()
    {
        lock (_gate)
        {
            _lastPollAt = clock.GetUtcNow();
            _gapOwed = false;
            _pollRequested = false;
        }
    }

    /// <summary>Asks for an extra poll (a garbled message was dropped).</summary>
    public void RequestPoll()
    {
        lock (_gate)
        {
            _pollRequested = true;
        }
    }

    public bool PollRequested
    {
        get
        {
            lock (_gate)
            {
                return _pollRequested;
            }
        }
    }

    public bool StreamsAlive
    {
        get
        {
            lock (_gate)
            {
                return !_gapOwed && _lastPollAt is { } at && clock.GetUtcNow() - at <= PollFreshness;
            }
        }
    }

    public StreamStatusView View()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            _reconnects.RemoveAll(t => now - t > TimeSpan.FromHours(24));
            return new StreamStatusView(_connected, _lastMessageAt, _lastPollAt, _reconnects.Count, counters.Dropped, counters.Applied, counters.Ignored, !_gapOwed && _lastPollAt is { } at && now - at <= PollFreshness);
        }
    }
}
