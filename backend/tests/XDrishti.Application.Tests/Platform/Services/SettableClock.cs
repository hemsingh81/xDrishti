using Microsoft.Extensions.Time.Testing;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>
/// A fake clock that can also be set back (the fake time provider only moves forward): timers fire as the clock moves forward, reading the clock gives the value last set. It also keeps the books of its timers - how many were
/// created and fired, and when the next one that is still alive is due - so that a test can move the clock <b>from one wake-up of the code under test to the next</b> instead of by guessed steps (see <see cref="NextTimerDue"/>).
/// The clock must be moved through <see cref="Advance"/>, <see cref="MoveTo"/> or <see cref="Jump"/> of this class (not through the base class): they share a gate with the creation of a timer, so that a timer is never
/// created while the time moves and the due time kept here is the provider's.
/// </summary>
internal sealed class SettableClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private readonly object _moveGate = new(); // outermost: held while the provider moves its time or makes a timer
    private readonly object _gate = new(); // the books below; the provider's own lock is never taken while holding it
    private readonly List<TimeSpan> _waits = [];
    private readonly List<(TimeSpan Due, TaskCompletionSource Seen)> _watchers = [];
    private readonly List<TrackedTimer> _timers = [];
    private long _timersCreated;
    private long _timersFired;
    private DateTimeOffset? _setBack;

    public void Jump(DateTimeOffset instant)
    {
        lock (_moveGate)
        {
            if (instant >= base.GetUtcNow())
            {
                _setBack = null;
                SetUtcNow(instant);
            }
            else
            {
                _setBack = instant;
            }
        }
    }

    /// <summary>Moves the clock forward and runs the timers that fall due.</summary>
    public new void Advance(TimeSpan delta)
    {
        lock (_moveGate)
        {
            base.Advance(delta);
        }
    }

    /// <summary>Moves the clock forward to <paramref name="instant"/> of the provider's time (not the set-back time of <see cref="GetUtcNow"/>); does nothing when the clock is already there.</summary>
    public void MoveTo(DateTimeOffset instant)
    {
        lock (_moveGate)
        {
            var delta = instant - base.GetUtcNow();
            if (delta > TimeSpan.Zero)
            {
                base.Advance(delta);
            }
        }
    }

    public override DateTimeOffset GetUtcNow() => _setBack ?? base.GetUtcNow();

    /// <summary>How long each timer created so far was asked to wait: a copy of the list as it is now (the code under test creates timers on its own thread while the test reads this).</summary>
    public IReadOnlyList<TimeSpan> Waits
    {
        get
        {
            lock (_gate)
            {
                return [.. _waits];
            }
        }
    }

    /// <summary>How many timers were created so far (a wait of the code under test is one: a delay, a periodic timer, a timeout).</summary>
    public long TimersCreated
    {
        get
        {
            lock (_gate)
            {
                return _timersCreated;
            }
        }
    }

    /// <summary>How many times a timer's callback has run so far (a periodic timer counts once for every period that passed). A callback can run after <c>Advance</c> has returned: see <see cref="HasTimerDueBy"/>.</summary>
    public long TimersFired
    {
        get
        {
            lock (_gate)
            {
                return _timersFired;
            }
        }
    }

    /// <summary>
    /// When the earliest timer that is still alive (not disposed, not a one-shot that has fired) is due next; null when the code under test is waiting for no time at all. Moving the clock to this instant, and not past it, wakes the
    /// code under test once and leaves it no time to see more than it should.
    /// </summary>
    public DateTimeOffset? NextTimerDue
    {
        get
        {
            var now = ProviderNow();
            DateTimeOffset? next = null;
            lock (_gate)
            {
                foreach (var timer in _timers)
                {
                    if (timer.NextDue is { } due && due > now && (next is null || due < next))
                    {
                        next = due;
                    }
                }
            }

            return next;
        }
    }

    /// <summary>
    /// True while a timer that is still alive should have fired by <paramref name="instant"/> and has not yet run its callback. The fake provider does not always run the callbacks inside <c>Advance</c>: when another thread is
    /// reading the clock at that moment (the code under test does, all the time) the callback is left to that thread and runs a moment later - or, on a machine that is oversubscribed, a good while later. Whoever moves the clock
    /// and then looks at what the timers did must wait until this is false.
    /// </summary>
    public bool HasTimerDueBy(DateTimeOffset instant)
    {
        lock (_gate)
        {
            return _timers.Any(t => t.NextDue is { } due && due <= instant);
        }
    }

    /// <summary>Completes when a timer of exactly this length is created (or was): the code under test is waiting that long.</summary>
    public Task WhenWaitingForAsync(TimeSpan due)
    {
        lock (_gate)
        {
            if (_waits.Contains(due))
            {
                return Task.CompletedTask;
            }

            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _watchers.Add((due, seen));
            return seen.Task;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        TrackedTimer tracked;
        lock (_moveGate)
        {
            tracked = new TrackedTimer(this, ProviderNow(), callback, state, dueTime, period);
        }

        // Announced only now that the provider holds the timer: whoever learns of the wait may move the clock at once and must find the timer there. It is counted before it can be seen, and both under the one lock the readers
        // take: a reader that has looked at the count and then finds the timer can tell that the timer is new (see FeedWorld.RunClockUntilAsync), and never meets it before it is counted.
        lock (_gate)
        {
            _timers.RemoveAll(t => t.NextDue is null);
            _timersCreated++;
            _timers.Add(tracked);
            _waits.Add(dueTime);
            foreach (var watcher in _watchers.Where(w => w.Due == dueTime))
            {
                watcher.Seen.TrySetResult();
            }
        }

        return tracked;
    }

    private DateTimeOffset ProviderNow() => base.GetUtcNow();

    private ITimer CreateInner(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => base.CreateTimer(callback, state, dueTime, period);

    private static DateTimeOffset? DueAt(DateTimeOffset now, TimeSpan dueTime) => dueTime >= TimeSpan.Zero ? now + dueTime : null;

    /// <summary>A timer of the fake provider, wrapped to keep count of its callbacks and of when it is due next.</summary>
    private sealed class TrackedTimer : ITimer
    {
        private readonly SettableClock _owner;
        private readonly ITimer _inner;
        private TimeSpan _period;
        private bool _disposed;

        public TrackedTimer(SettableClock owner, DateTimeOffset now, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _owner = owner;
            _period = period;
            NextDue = DueAt(now, dueTime);
            _inner = owner.CreateInner(
                s =>
                {
                    Fired();
                    callback(s);
                },
                state,
                dueTime,
                period);
        }

        public DateTimeOffset? NextDue { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._moveGate)
            {
                lock (_owner._gate)
                {
                    if (!_disposed)
                    {
                        _period = period;
                        NextDue = DueAt(_owner.ProviderNow(), dueTime);
                        if (!_owner._timers.Contains(this))
                        {
                            _owner._timers.Add(this);
                        }
                    }
                }

                return _inner.Change(dueTime, period);
            }
        }

        public void Dispose()
        {
            Forget();
            _inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Forget();
            return _inner.DisposeAsync();
        }

        private void Fired()
        {
            lock (_owner._gate)
            {
                _owner._timersFired++;
                NextDue = _period > TimeSpan.Zero && NextDue is { } due ? due + _period : null;
            }
        }

        private void Forget()
        {
            lock (_owner._gate)
            {
                _disposed = true;
                NextDue = null;
            }
        }
    }
}
