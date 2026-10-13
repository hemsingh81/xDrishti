using Microsoft.Extensions.Time.Testing;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>A fake clock that can also be set back (the fake time provider only moves forward): timers fire as the clock moves forward, reading the clock gives the value last set.</summary>
internal sealed class SettableClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private DateTimeOffset? _setBack;

    public void Jump(DateTimeOffset instant)
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

    public override DateTimeOffset GetUtcNow() => _setBack ?? base.GetUtcNow();

    /// <summary>How long each timer created so far was asked to wait.</summary>
    public List<TimeSpan> Waits { get; } = [];

    private readonly List<(TimeSpan Due, TaskCompletionSource Seen)> _watchers = [];

    /// <summary>Completes when a timer of exactly this length is created (or was): the code under test is waiting that long.</summary>
    public Task WhenWaitingForAsync(TimeSpan due)
    {
        lock (_watchers)
        {
            if (Waits.Contains(due))
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
        lock (_watchers)
        {
            Waits.Add(dueTime);
            foreach (var watcher in _watchers.Where(w => w.Due == dueTime))
            {
                watcher.Seen.TrySetResult();
            }
        }

        return base.CreateTimer(callback, state, dueTime, period);
    }
}
