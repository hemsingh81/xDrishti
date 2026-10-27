using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>
/// The drafting jobs in progress (spec P9-07 Data: "a job in progress is held in memory; a crash loses it, which is stated"). A singleton. A job runs on its own scope after the request that started it has been answered, holds the model slot until it ends and removes itself.
/// </summary>
public sealed class DraftJobs
{
    private readonly ConcurrentDictionary<long, string> _running = new();
    private readonly ConcurrentDictionary<long, Task> _tasks = new();
    private readonly IServiceScopeFactory _scopes;

    public DraftJobs(IServiceScopeFactory scopes) => _scopes = scopes;

    /// <summary>The idea of a job in progress, or null.</summary>
    public string? IdeaOf(long id) => _running.TryGetValue(id, out var idea) ? idea : null;

    /// <summary>How many jobs are in progress.</summary>
    public int Count => _running.Count;

    /// <summary>Starts the body on a new scope and returns at once. <paramref name="slot"/> is released when the body ends, however it ends.</summary>
    internal void Launch(long id, string idea, IDisposable slot, Func<IServiceProvider, Task> body, Action<long> onCrash)
    {
        _running[id] = idea;
        _tasks[id] = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await body(scope.ServiceProvider);
            }
#pragma warning disable CA1031 // a background job must not take the host down; the crash is logged by id and the in-memory state is cleared
            catch (Exception)
#pragma warning restore CA1031
            {
                onCrash(id);
            }
            finally
            {
                _running.TryRemove(id, out _);
                slot.Dispose();
            }
        });
    }

    /// <summary>Waits for every job started so far (tests and a clean shutdown).</summary>
    public Task WhenIdleAsync() => Task.WhenAll(_tasks.Values);
}
