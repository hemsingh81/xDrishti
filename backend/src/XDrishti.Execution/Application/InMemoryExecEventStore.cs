using XDrishti.Domain.Common;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application;

/// <summary>
/// The in-memory event log of intents: insert-only events, the live state as a fold. The database store, with the same rules enforced by constraints, is P12-01b. A refused event stores nothing.
/// </summary>
public sealed class InMemoryExecEventStore
{
    private readonly Dictionary<long, List<ExecEvent>> _events = [];
    private readonly Dictionary<long, ExecIntent> _live = [];
    private readonly Lock _gate = new();

    public Result Start(ExecIntent intent)
    {
        lock (_gate)
        {
            if (!_live.TryAdd(intent.Id, intent))
            {
                return Result.Failure(Error.Conflict("execution.event_conflict", "The intent already exists."));
            }

            _events[intent.Id] = [];
            return Result.Success();
        }
    }

    public Result Append(long intentId, ExecEvent e)
    {
        lock (_gate)
        {
            if (!_live.TryGetValue(intentId, out var current))
            {
                return Result.Failure(Error.NotFound("execution.intent_unknown", "No such intent."));
            }

            var next = current.Apply(e);
            if (next.IsFailure)
            {
                return Result.Failure(next.Error);
            }

            _events[intentId].Add(e);
            _live[intentId] = next.Value;
            return Result.Success();
        }
    }

    public ExecIntent? Live(long intentId)
    {
        lock (_gate)
        {
            return _live.GetValueOrDefault(intentId);
        }
    }

    public IReadOnlyList<ExecEvent> Events(long intentId)
    {
        lock (_gate)
        {
            return [.. _events.GetValueOrDefault(intentId) ?? []];
        }
    }

    /// <summary>Folds the stored events of an intent from its first state. The projection is this fold: it equals <see cref="Live"/>.</summary>
    public Result<ExecIntent> Rebuild(ExecIntent seed, long intentId)
    {
        var state = Result.Success(seed);
        foreach (var e in Events(intentId))
        {
            state = state.Value.Apply(e);
            if (state.IsFailure)
            {
                return state;
            }
        }

        return state;
    }
}
