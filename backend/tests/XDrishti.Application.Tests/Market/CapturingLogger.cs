using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Tests.Market;

/// <summary>Records log events so tests can wait for one (events are logged right before the logged code goes on, in the same call).</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly object _gate = new();

    private readonly List<(int Event, string Message)> _events = [];

    private readonly List<(int Event, int Count, TaskCompletionSource Done)> _waiters = [];

    public IReadOnlyList<(int Event, string Message)> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>Completes when the event has been logged <paramref name="count"/> times in total.</summary>
    public Task WaitForAsync(int eventId, int count = 1)
    {
        lock (_gate)
        {
            if (_events.Count(e => e.Event == eventId) >= count)
            {
                return Task.CompletedTask;
            }

            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((eventId, count, done));
            return done.Task;
        }
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            _events.Add((eventId.Id, formatter(state, exception)));
            foreach (var waiter in _waiters.Where(w => _events.Count(e => e.Event == w.Event) >= w.Count).ToList())
            {
                waiter.Done.TrySetResult();
                _waiters.Remove(waiter);
            }
        }
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;
}
