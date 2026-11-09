using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Platform.Services;

/// <summary>
/// What was missed over several days and the order it can be made up (ADR 0240, spec AU-01). Pure: the services, their last runs, the calendar and the instant are passed in; nothing runs and nothing is read.
/// A service that is paused or has catch-up off is not missed (ADR 0018). A dependency cycle is an error, never a guess.
/// </summary>
public static class RecoveryPlanner
{
    public const int MinSessions = 1;

    public const int MaxSessionsLimit = 250;

    /// <summary>The default reach in sessions (equal to <c>Data:Gaps:LookbackSessions</c>).</summary>
    public const int DefaultMaxSessions = 30;

    /// <summary>The end-of-day service: everything that follows it (and is not the nightly pipeline) is the learning stage. Pinned to the service's id by a test.</summary>
    public const string EodId = "eod";

    /// <summary>The nightly service: it and everything after it run last, and it runs only before its review cutoff. Pinned to the service's id by a test.</summary>
    public const string NightlyId = "nightly";

    private const int SearchDays = 400;

    private const int CalendarLookbackDays = 3660;

    public const string LiveObservationReason = "needs a live observation at the time; made up later it would be a record that was not seen then";

    public const string CutoffPassedReason = "cutoff passed";

    public const string OutOfReachReason = "older than the recovery reach (Recovery:MaxSessions); the Data backfill is the way";

    public static Result<RecoveryPlan> Plan(IReadOnlyList<RecoveryInput> services, TradingCalendar calendar, DateTimeOffset now, int maxSessions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(calendar);
        if (maxSessions is < MinSessions or > MaxSessionsLimit)
        {
            return Result.Failure<RecoveryPlan>(Error.Validation("recovery.reach_invalid", $"Recovery:MaxSessions must be {MinSessions} to {MaxSessionsLimit}."));
        }

        var byId = new Dictionary<string, RecoveryInput>(StringComparer.Ordinal);
        foreach (var input in services)
        {
            if (!byId.TryAdd(input.Definition.Id, input))
            {
                return Result.Failure<RecoveryPlan>(Error.Validation("recovery.duplicate_service", $"Service '{input.Definition.Id}' is registered twice."));
            }

            if (input.Definition.Constraints.RecoveryClass is null)
            {
                return Result.Failure<RecoveryPlan>(Error.Validation("recovery.unclassified", $"Service '{input.Definition.Id}' declares no recovery class."));
            }
        }

        var dependencies = services.ToDictionary(s => s.Definition.Id, s => DependenciesOf(s, byId), StringComparer.Ordinal);
        var stages = StagesOf(dependencies);
        if (Order(dependencies, stages) is not { } order)
        {
            return Result.Failure<RecoveryPlan>(Error.Validation("recovery.cycle", "The services' MustFollow and 'after' chains form a cycle: " + string.Join(", ", CycleMembers(dependencies)) + "."));
        }

        var lastClosed = calendar.LastClosedSession(now);
        var reachStart = ReachStart(calendar, lastClosed, maxSessions);
        var steps = new List<RecoveryStep>();
        var items = new List<RecoveryItem>();
        foreach (var id in order)
        {
            var input = byId[id];
            if (!input.Settings.Enabled || !input.Settings.CatchUp)
            {
                continue;
            }

            var due = DueOccurrences(input, byId, calendar, now, reachStart);
            if (due.Count > 0)
            {
                Classify(input, due, calendar, now, lastClosed, reachStart, steps, items);
            }
        }

        return Result.Success(new RecoveryPlan(steps, items));
    }

    private static void Classify(RecoveryInput input, List<(DateOnly Session, DateTimeOffset Due)> due, TradingCalendar calendar, DateTimeOffset now, DateOnly lastClosed, DateOnly reachStart, List<RecoveryStep> steps, List<RecoveryItem> items)
    {
        var id = input.Definition.Id;
        var cls = input.Definition.Constraints.RecoveryClass!.Value;
        var inReach = new List<(DateOnly Session, DateTimeOffset Due)>();
        foreach (var d in due)
        {
            if (d.Session < reachStart)
            {
                items.Add(new RecoveryItem(id, d.Session, d.Due, RecoveryItemStatus.OutOfReach, OutOfReachReason));
            }
            else
            {
                inReach.Add(d);
            }
        }

        if (inReach.Count == 0)
        {
            return;
        }

        switch (cls)
        {
            case RecoveryClass.TimeBound:
                AddAll(items, id, inReach, RecoveryItemStatus.NotRecoverable, LiveObservationReason);
                break;
            case RecoveryClass.Convergent:
                AddAll(items, id, inReach, RecoveryItemStatus.Planned, null);
                steps.Add(new RecoveryStep(id, cls, inReach.Select(d => d.Session).ToList()));
                break;
            case RecoveryClass.Dated:
                AddAll(items, id, inReach, RecoveryItemStatus.Planned, null);
                steps.AddRange(inReach.Select(d => new RecoveryStep(id, cls, [d.Session])));
                break;
            case RecoveryClass.LatestOnly:
                if (string.Equals(id, NightlyId, StringComparison.Ordinal) && !CutoffAhead(calendar, now, lastClosed))
                {
                    AddAll(items, id, inReach, RecoveryItemStatus.NotRecoverable, CutoffPassedReason);
                    break;
                }

                AddAll(items, id, inReach.Take(inReach.Count - 1), RecoveryItemStatus.Superseded, "a later run of this service covers it");
                var latest = inReach[^1];
                items.Add(new RecoveryItem(id, latest.Session, latest.Due, RecoveryItemStatus.Planned, null));
                steps.Add(new RecoveryStep(id, cls, [latest.Session]));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input), cls, "Unknown recovery class.");
        }
    }

    private static void AddAll(List<RecoveryItem> items, string id, IEnumerable<(DateOnly Session, DateTimeOffset Due)> due, RecoveryItemStatus status, string? reason) =>
        items.AddRange(due.Select(d => new RecoveryItem(id, d.Session, d.Due, status, reason)));

    /// <summary>The nightly plan is for the session after the latest closed one; it can only be made while that session's review cutoff is still ahead. No next session in the calendar means it cannot be said, so not.</summary>
    private static bool CutoffAhead(TradingCalendar calendar, DateTimeOffset now, DateOnly lastClosed) =>
        NightlyRules.NextSession(calendar, lastClosed) is { } next && NightlyRules.ReviewCutoff(next) > now;

    /// <summary>The first date of the reach: the <paramref name="maxSessions"/>th trading session counting back from the latest closed one.</summary>
    private static DateOnly ReachStart(TradingCalendar calendar, DateOnly lastClosed, int maxSessions)
    {
        var date = lastClosed;
        var count = 1;
        for (var back = 1; back <= CalendarLookbackDays && count < maxSessions; back++)
        {
            var candidate = lastClosed.AddDays(-back);
            if (calendar.IsTradingDay(candidate))
            {
                date = candidate;
                count++;
            }
        }

        return date;
    }

    /// <summary>The missed occurrences, oldest first, one per India date: due by now and with no run started at or after it. A service that follows another is due when the one it follows is.</summary>
    private static List<(DateOnly Session, DateTimeOffset Due)> DueOccurrences(RecoveryInput input, Dictionary<string, RecoveryInput> byId, TradingCalendar calendar, DateTimeOffset now, DateOnly reachStart)
    {
        var due = new List<(DateOnly, DateTimeOffset)>();
        var leader = input;
        while (leader.Settings.Schedule.Type == ScheduleType.After)
        {
            if (leader.Settings.Schedule.After is not { } next || !byId.TryGetValue(next, out var found))
            {
                return due;
            }

            leader = found;
        }

        var schedule = leader.Settings.Schedule;
        var today = IndiaTime.Today(now);
        var last = input.LastRunStartedAt;
        var limit = today.AddDays(-SearchDays);
        var floor = last is null ? reachStart : DateOnly.FromDayNumber(Math.Max(IndiaTime.Today(last.Value).DayNumber, limit.DayNumber));

        for (var date = floor; date <= today; date = date.AddDays(1))
        {
            var occurrences = ScheduleCalculator.OccurrencesOn(schedule, date, calendar, leader.Definition.Constraints.NotBeforeSessionEnd);
            if (occurrences.Count > 0 && occurrences[0] <= now && (last is null || last < occurrences[0]))
            {
                due.Add((date, occurrences[0]));
            }
        }

        return due;
    }

    private static List<string> DependenciesOf(RecoveryInput input, Dictionary<string, RecoveryInput> byId)
    {
        var ids = new List<string>(input.Definition.Constraints.MustFollow);
        if (input.Settings.Schedule is { Type: ScheduleType.After, After: { } after })
        {
            ids.Add(after);
        }

        return ids.Where(byId.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>0 data, 1 learning (follows the end-of-day service), 2 the nightly service and what follows it. Derived from the chains, so it needs no table.</summary>
    private static Dictionary<string, int> StagesOf(Dictionary<string, List<string>> dependencies)
    {
        var stages = dependencies.Keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        Mark(NightlyId, 2);
        Mark(EodId, 1);
        return stages;

        void Mark(string root, int stage)
        {
            if (!dependencies.ContainsKey(root))
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var (id, deps) in dependencies)
                {
                    if (deps.Contains(current, StringComparer.Ordinal) && seen.Add(id))
                    {
                        stages[id] = Math.Max(stages[id], stage);
                        stack.Push(id);
                    }
                }
            }

            if (stage == 2)
            {
                stages[root] = 2;
            }
        }
    }

    /// <summary>Kahn's order; of the services that are ready, the lowest stage first, then by id. Null when a cycle leaves some unordered.</summary>
    private static List<string>? Order(Dictionary<string, List<string>> dependencies, Dictionary<string, int> stages)
    {
        var remaining = dependencies.ToDictionary(d => d.Key, d => d.Value.Count, StringComparer.Ordinal);
        var ready = new SortedSet<(int Stage, string Id)>(Comparer<(int Stage, string Id)>.Create((a, b) => a.Stage != b.Stage ? a.Stage.CompareTo(b.Stage) : string.CompareOrdinal(a.Id, b.Id)));
        foreach (var (id, count) in remaining.Where(r => r.Value == 0))
        {
            ready.Add((stages[id], id));
        }

        var order = new List<string>(remaining.Count);
        while (ready.Count > 0)
        {
            var next = ready.Min;
            ready.Remove(next);
            order.Add(next.Id);
            foreach (var (id, deps) in dependencies)
            {
                if (deps.Contains(next.Id, StringComparer.Ordinal) && --remaining[id] == 0)
                {
                    ready.Add((stages[id], id));
                }
            }
        }

        return order.Count == dependencies.Count ? order : null;
    }

    private static IEnumerable<string> CycleMembers(Dictionary<string, List<string>> dependencies)
    {
        var left = new HashSet<string>(dependencies.Keys, StringComparer.Ordinal);
        bool changed;
        do
        {
            var leaves = left.Where(id => !dependencies.Any(d => left.Contains(d.Key) && d.Value.Contains(id, StringComparer.Ordinal))).ToList();
            var roots = left.Where(id => dependencies[id].All(d => !left.Contains(d))).ToList();
            changed = leaves.Count + roots.Count > 0;
            foreach (var id in leaves.Concat(roots))
            {
                left.Remove(id);
            }
        }
        while (changed);
        return left.Order(StringComparer.Ordinal);
    }
}
