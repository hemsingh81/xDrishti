using System.Text.Json;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>Turns the body of a save or preview into settings: reads the text, fills the parameters the body left out, reports what cannot be read and checks the result against the whole set.</summary>
internal static class ServiceInputChecks
{
    /// <summary>
    /// The settings of the input, or null when the schedule cannot be read (the reasons are in <paramref name="issues"/>). The values are checked by <see cref="ScheduleRules"/>,
    /// which needs a schedule: an unreadable one is replaced by the definition's own so that the other fields are still checked in the same go.
    /// </summary>
    private static ServiceSettings? Read(ServiceDefinition definition, ServiceScheduleInput input, List<ScheduleIssue> issues, out ServiceSettings forChecks)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(issues);
        Schedule? schedule = null;
        if (input.Schedule is null)
        {
            issues.Add(ScheduleIssue.Error(definition.Id, "schedule.type", ScheduleIssueCodes.TypeInvalid, "Choose how the service is scheduled."));
        }
        else
        {
            schedule = input.Schedule.Parse(definition.Id, issues);
        }

        var parameters = new Dictionary<string, JsonElement>(input.Parameters ?? new Dictionary<string, JsonElement>(), StringComparer.Ordinal);
        foreach (var parameter in definition.Parameters.Where(p => !parameters.ContainsKey(p.Name)))
        {
            parameters[parameter.Name] = parameter.Default;
        }

        forChecks = new ServiceSettings(input.Enabled, schedule ?? definition.Defaults.Schedule, input.Retries, input.TimeoutMinutes, input.CatchUp, input.NotifyOnFailure, parameters);
        return schedule is null ? null : forChecks;
    }

    /// <summary>
    /// Reads the input for <paramref name="service"/> and checks it with <see cref="ScheduleRules"/> over the set in which it replaces the service's settings. <paramref name="parsed"/> is the settings when the
    /// schedule could be read (null otherwise). Returns every finding: the unreadable fields, then the errors and warnings of the rules (those about the schedule of the service itself only when it was readable).
    /// </summary>
    public static List<ScheduleIssue> Evaluate(IReadOnlyList<EffectiveService> all, EffectiveService service, ServiceScheduleInput input, out ServiceSettings? parsed)
    {
        var issues = new List<ScheduleIssue>();
        parsed = Read(service.Definition, input, issues, out var candidate);
        var readable = parsed is not null;
        issues.AddRange(ScheduleRules.Evaluate(ServiceMapper.Subjects(all, service.Id, candidate))
            .Where(i => readable || i.ServiceId != service.Id || !i.Field.StartsWith("schedule.", StringComparison.Ordinal)));
        return issues;
    }
}
