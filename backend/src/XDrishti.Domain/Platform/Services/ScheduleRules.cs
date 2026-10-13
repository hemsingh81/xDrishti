using System.Globalization;

namespace XDrishti.Domain.Platform.Services;

/// <summary>
/// The checks of every save and preview (ADR 0018 §6, design doc 04 §3.1): pure rules over the <b>whole</b> set of registered services, so a change to one is checked against the others and a
/// finding is reported on the service it is about (moving the end-of-day time after a dependent service's own time is reported on the dependent). <b>Errors</b> block saving: a type the
/// service does not allow, an unreadable value, no day, a window that does not start before it ends, an interval outside 1–120 minutes or longer than its window, a run-after chain that is
/// circular or ends in an always-up service, retries or timeout out of bounds, an unknown parameter or one out of its bounds. <b>Warnings</b> are shown and allowed: the constraints of the
/// definitions (a start before or after the advised time, a window that does not cover the part of the day it must, a run during market hours that must not, a service that should follow
/// another and does not), a run-after chain of more than three, two services at the same minute. Only registered services are in the set, so the rules of a service that does not exist yet
/// are never evaluated; warnings are given for services that are on only.
/// </summary>
public static class ScheduleRules
{
    /// <summary>A chain longer than this many services is warned about.</summary>
    public const int MaxChain = 3;

    public const int MinInterval = 1;

    public const int MaxInterval = 120;

    public static IReadOnlyList<ScheduleIssue> Evaluate(IReadOnlyList<RuleSubject> set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var byId = set.ToDictionary(s => s.Definition.Id, StringComparer.Ordinal);
        var issues = new List<ScheduleIssue>();
        foreach (var subject in set)
        {
            CheckValues(subject, issues);
            CheckChain(subject, byId, issues);
        }

        foreach (var subject in set.Where(s => s.Settings.Enabled))
        {
            CheckConstraints(subject, byId, issues);
        }

        CheckSameMinute([.. set.Where(s => s.Settings.Enabled)], issues);
        return [.. issues.OrderBy(i => i.Severity).ThenBy(i => i.ServiceId, StringComparer.Ordinal).ThenBy(i => i.Field, StringComparer.Ordinal).ThenBy(i => i.Code, StringComparer.Ordinal)];
    }

    private static void CheckValues(RuleSubject subject, List<ScheduleIssue> issues)
    {
        var (definition, settings) = (subject.Definition, subject.Settings);
        var id = definition.Id;
        var schedule = settings.Schedule;
        if (!definition.AllowedTypes.Contains(schedule.Type))
        {
            issues.Add(ScheduleIssue.Error(id, "schedule.type", ScheduleIssueCodes.TypeNotAllowed,
                $"{definition.Name} cannot be scheduled as {schedule.Type.ToCode()}; choose {string.Join(", ", definition.AllowedTypes.Select(t => t.ToCode()))}."));
        }

        if (settings.Retries is < ServiceSettings.MinRetries or > ServiceSettings.MaxRetries)
        {
            issues.Add(ScheduleIssue.Error(id, "retries", ScheduleIssueCodes.RetriesRange, $"Use {ServiceSettings.MinRetries} to {ServiceSettings.MaxRetries} retries."));
        }

        if (settings.TimeoutMinutes is < ServiceSettings.MinTimeoutMinutes or > ServiceSettings.MaxTimeoutMinutes)
        {
            issues.Add(ScheduleIssue.Error(id, "timeoutMinutes", ScheduleIssueCodes.TimeoutRange, $"Use a timeout of {ServiceSettings.MinTimeoutMinutes} to {ServiceSettings.MaxTimeoutMinutes} minutes."));
        }

        CheckSchedule(id, schedule, issues);
        CheckParameters(subject, issues);
    }

    private static void CheckSchedule(string id, Schedule schedule, List<ScheduleIssue> issues)
    {
        if (schedule.Type is ScheduleType.Daily or ScheduleType.Window or ScheduleType.Interval && !schedule.TradingDaysOnly && schedule.Days == Weekdays.None)
        {
            issues.Add(ScheduleIssue.Error(id, "schedule.days", ScheduleIssueCodes.NoDay, "Choose at least one day, or trading days."));
        }

        if (schedule.Type is ScheduleType.Window or ScheduleType.Interval && schedule is { From: { } from, To: { } to })
        {
            if (from >= to)
            {
                issues.Add(ScheduleIssue.Error(id, "schedule.to", ScheduleIssueCodes.WindowOrder, "The end must be after the start."));
            }
            else if (schedule is { Type: ScheduleType.Interval, EveryMinutes: { } every } && every >= MinInterval && every <= MaxInterval && every > (to - from).TotalMinutes)
            {
                issues.Add(ScheduleIssue.Error(id, "schedule.everyMinutes", ScheduleIssueCodes.IntervalExceedsWindow, "The interval is longer than the window."));
            }
        }

        if (schedule.Type == ScheduleType.Interval && schedule.EveryMinutes is not (>= MinInterval and <= MaxInterval))
        {
            issues.Add(ScheduleIssue.Error(id, "schedule.everyMinutes", ScheduleIssueCodes.IntervalRange, $"Use an interval of {MinInterval} to {MaxInterval} minutes."));
        }

        if (schedule.Type == ScheduleType.After && string.IsNullOrWhiteSpace(schedule.After))
        {
            issues.Add(ScheduleIssue.Error(id, "schedule.after", ScheduleIssueCodes.AfterMissing, "Choose the service to run after."));
        }
    }

    private static void CheckParameters(RuleSubject subject, List<ScheduleIssue> issues)
    {
        foreach (var (name, value) in subject.Settings.Parameters)
        {
            var parameter = subject.Definition.FindParameter(name);
            if (parameter is null)
            {
                issues.Add(ScheduleIssue.Error(subject.Definition.Id, $"parameters.{Echo(name)}", ScheduleIssueCodes.ParameterUnknown, $"{subject.Definition.Name} has no parameter '{Echo(name)}'."));
            }
            else if (!parameter.Accepts(value, out var problem))
            {
                issues.Add(ScheduleIssue.Error(subject.Definition.Id, $"parameters.{name}", ScheduleIssueCodes.ParameterInvalid, $"{parameter.Label}: {problem}"));
            }
        }
    }

    /// <summary>The run-after links of one service: unknown or always-up target, circular chain (reported on every service of the cycle), long chain.</summary>
    private static void CheckChain(RuleSubject subject, Dictionary<string, RuleSubject> byId, List<ScheduleIssue> issues)
    {
        var id = subject.Definition.Id;
        if (subject.Settings.Schedule is not { Type: ScheduleType.After, After: { Length: > 0 } target })
        {
            return;
        }

        if (target == id)
        {
            issues.Add(ScheduleIssue.Error(id, "schedule.after", ScheduleIssueCodes.ChainCircular, $"{subject.Definition.Name} cannot run after itself."));
            return;
        }

        if (!byId.TryGetValue(target, out var predecessor))
        {
            issues.Add(ScheduleIssue.Error(id, "schedule.after", ScheduleIssueCodes.AfterUnknown, $"'{Echo(target)}' is not a service that can be run after."));
            return;
        }

        if (predecessor.Settings.Schedule.Type == ScheduleType.Continuous)
        {
            issues.Add(ScheduleIssue.Error(id, "schedule.after", ScheduleIssueCodes.ChainTargetContinuous, $"{predecessor.Definition.Name} is always on and never finishes, so nothing can run after it.", target));
            return;
        }

        var chain = ChainOf(id, byId, out var circular, out var loopsElsewhere);
        if (loopsElsewhere)
        {
            return; // downstream of a circle: the members of the circle carry the error
        }

        if (circular)
        {
            issues.Add(ScheduleIssue.Error(id, "schedule.after", ScheduleIssueCodes.ChainCircular, $"The services run after each other in a circle: {string.Join(" → ", chain.Select(c => byId[c].Definition.Name))}.", target));
        }
        else if (subject.Settings.Enabled && chain.Count > MaxChain)
        {
            issues.Add(ScheduleIssue.Warning(id, "schedule.after", ScheduleIssueCodes.ChainLong, $"{subject.Definition.Name} ends a chain of {chain.Count} services that run after each other (more than {MaxChain})."));
        }
    }

    /// <summary>
    /// The services a service runs after, starting at the first of the chain and ending with the service itself; <paramref name="circular"/> when the walk came back to the service,
    /// <paramref name="loopsElsewhere"/> when it ran into a circle that does not include it.
    /// </summary>
    private static List<string> ChainOf(string id, Dictionary<string, RuleSubject> byId, out bool circular, out bool loopsElsewhere)
    {
        var chain = new List<string> { id };
        var current = id;
        circular = false;
        loopsElsewhere = false;
        while (byId.TryGetValue(current, out var subject) && subject.Settings.Schedule is { Type: ScheduleType.After, After: { Length: > 0 } next } && byId.ContainsKey(next))
        {
            if (next == id)
            {
                circular = true;
                break;
            }

            if (chain.Contains(next, StringComparer.Ordinal))
            {
                loopsElsewhere = true;
                break;
            }

            chain.Insert(0, next);
            current = next;
        }

        return chain;
    }

    private static void CheckConstraints(RuleSubject subject, Dictionary<string, RuleSubject> byId, List<ScheduleIssue> issues)
    {
        var (definition, settings) = (subject.Definition, subject.Settings);
        var (id, schedule, constraints) = (definition.Id, settings.Schedule, definition.Constraints);
        var timeField = schedule.Type is ScheduleType.Window or ScheduleType.Interval ? "schedule.from" : "schedule.time";
        if (schedule.StartTime is { } start)
        {
            if (constraints.StartNotBefore is { } earliest && start < earliest)
            {
                issues.Add(ScheduleIssue.Warning(id, timeField, ScheduleIssueCodes.StartTooEarly, $"{definition.Name} starts at {Hm(start)}, before {Hm(earliest)}. {constraints.StartAdvice}".TrimEnd()));
            }

            if (constraints.StartNotAfter is { } latest && start > latest)
            {
                issues.Add(ScheduleIssue.Warning(id, timeField, ScheduleIssueCodes.StartTooLate, $"{definition.Name} starts at {Hm(start)}, after {Hm(latest)}. {constraints.StartAdvice}".TrimEnd()));
            }
        }

        if (constraints.MustCover is { } cover && schedule.Type is ScheduleType.Window or ScheduleType.Interval && schedule is { From: { } from, To: { } to } && (from > cover.From || to < cover.To))
        {
            issues.Add(ScheduleIssue.Warning(id, from > cover.From ? "schedule.from" : "schedule.to", ScheduleIssueCodes.WindowNotCovering,
                $"{definition.Name} should be up from {Hm(cover.From)} to {Hm(cover.To)}; the window is {Hm(from)} to {Hm(to)}."));
        }

        if (constraints.MustNotOverlap is { } quiet && RunsOnMarketDays(schedule) && Overlaps(schedule, quiet))
        {
            issues.Add(ScheduleIssue.Warning(id, schedule.Type == ScheduleType.Continuous ? "schedule.type" : timeField, ScheduleIssueCodes.OverlapsMarketHours,
                $"{definition.Name} should not run during market hours ({Hm(quiet.From)} to {Hm(quiet.To)})."));
        }

        foreach (var predecessorId in constraints.MustFollow)
        {
            if (byId.TryGetValue(predecessorId, out var predecessor) && predecessor.Settings.Enabled && !Follows(subject, predecessor, byId))
            {
                issues.Add(ScheduleIssue.Warning(id, schedule.Type == ScheduleType.After ? "schedule.after" : timeField, ScheduleIssueCodes.MustFollow,
                    $"{definition.Name} should run after {predecessor.Definition.Name}.", predecessorId));
            }
        }
    }

    /// <summary>Whether the service runs after the other: it is in the other's run-after chain, or its own time is not before the other's (when the days overlap). Unknown timing counts as following.</summary>
    private static bool Follows(RuleSubject subject, RuleSubject predecessor, Dictionary<string, RuleSubject> byId)
    {
        var schedule = subject.Settings.Schedule;
        if (schedule.Type == ScheduleType.After)
        {
            return ChainOf(subject.Definition.Id, byId, out _, out _).Contains(predecessor.Definition.Id, StringComparer.Ordinal);
        }

        var other = predecessor.Settings.Schedule;
        if (schedule.StartTime is not { } own || other.StartTime is not { } theirs || (schedule.DaySet & other.DaySet) == Weekdays.None)
        {
            return true;
        }

        return own >= theirs;
    }

    private static bool RunsOnMarketDays(Schedule schedule) => schedule.TradingDaysOnly || (schedule.DaySet & Weekdays.MondayToFriday) != Weekdays.None;

    private static bool Overlaps(Schedule schedule, TimeRange range) => schedule switch
    {
        { Type: ScheduleType.Daily or ScheduleType.Weekly, Time: { } time } => range.Contains(time),
        { Type: ScheduleType.Window or ScheduleType.Interval, From: { } from, To: { } to } => range.Intersects(from, to),
        { Type: ScheduleType.Continuous } => true,
        _ => false,
    };

    private static void CheckSameMinute(List<RuleSubject> enabled, List<ScheduleIssue> issues)
    {
        var timed = enabled.Where(s => s.Settings.Schedule is { Type: ScheduleType.Daily or ScheduleType.Weekly, Time: not null }).ToList();
        for (var i = 0; i < timed.Count; i++)
        {
            for (var j = i + 1; j < timed.Count; j++)
            {
                var (a, b) = (timed[i], timed[j]);
                if (a.Settings.Schedule.Time == b.Settings.Schedule.Time && (a.Settings.Schedule.DaySet & b.Settings.Schedule.DaySet) != Weekdays.None)
                {
                    var at = Hm(a.Settings.Schedule.Time!.Value);
                    issues.Add(ScheduleIssue.Warning(a.Definition.Id, "schedule.time", ScheduleIssueCodes.SameMinute, $"{a.Definition.Name} and {b.Definition.Name} both start at {at}.", b.Definition.Id));
                    issues.Add(ScheduleIssue.Warning(b.Definition.Id, "schedule.time", ScheduleIssueCodes.SameMinute, $"{b.Definition.Name} and {a.Definition.Name} both start at {at}.", a.Definition.Id));
                }
            }
        }
    }

    /// <summary>A name from the request as it is repeated in a message: no control characters, at most 40 characters.</summary>
    private static string Echo(string text)
    {
        var clean = new string([.. text.Where(c => !char.IsControl(c))]);
        return clean.Length <= 40 ? clean : clean[..40] + "…";
    }

    private static string Hm(TimeOnly time) => time.ToString(ScheduleSpec.TimeFormat, CultureInfo.InvariantCulture);
}
