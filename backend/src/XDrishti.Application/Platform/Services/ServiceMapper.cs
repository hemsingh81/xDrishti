using XDrishti.Application.Live;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>Builds the views of the services from the settings, the latest runs, the findings and the calendar. Pure.</summary>
internal static class ServiceMapper
{
    public static ServiceView ToView(
        EffectiveService service,
        ServiceRun? lastRun,
        bool running,
        IEnumerable<ScheduleIssue> issues,
        TradingCalendar calendar,
        DateTimeOffset now,
        Func<string, string> nameOf,
        LiveFeedReading? external = null)
    {
        var (definition, settings) = (service.Definition, service.Settings);
        var status = external?.Status ?? (running ? "running" : !settings.Enabled ? "paused" : lastRun is { Status: ServiceRunStatus.Failed } ? "failed" : "healthy");
        var nextRun = settings.Enabled ? ScheduleCalculator.NextAfter(settings.Schedule, now, calendar, service.NotBeforeSessionEnd) : null;
        var defaults = definition.Defaults;
        var isDefault = (settings with { Parameters = definition.WithDefaults(settings.Parameters) }).Same(defaults with { Parameters = definition.WithDefaults(defaults.Parameters) });
        return new ServiceView(
            definition.Id,
            definition.Name,
            definition.Description,
            definition.RunsAs,
            false,
            null,
            settings.Enabled,
            status,
            ScheduleSpec.Of(settings.Schedule),
            ScheduleWords.Describe(settings.Schedule, nameOf, service.NotBeforeSessionEnd),
            [.. definition.AllowedTypes.Select(t => t.ToCode())],
            new ExecutionView(settings.Retries, settings.TimeoutMinutes, settings.CatchUp, settings.NotifyOnFailure),
            [.. definition.Parameters.Select(p => ParameterOf(p, settings))],
            lastRun is null ? null : ServiceRunView.From(lastRun),
            nextRun,
            [.. issues.Where(i => i.ServiceId == definition.Id).Select(ServiceIssueView.From)],
            isDefault,
            service.Revision,
            external?.Live);
    }

    public static IReadOnlyList<RuleSubject> Subjects(IEnumerable<EffectiveService> services) => [.. services.Select(s => new RuleSubject(s.Definition, s.Settings))];

    /// <summary>The set with one service's settings replaced (a candidate being saved or previewed).</summary>
    public static IReadOnlyList<RuleSubject> Subjects(IEnumerable<EffectiveService> services, string serviceId, ServiceSettings candidate) =>
        [.. services.Select(s => new RuleSubject(s.Definition, s.Id == serviceId ? candidate : s.Settings))];

    private static ParameterView ParameterOf(ParameterDefinition parameter, ServiceSettings settings) =>
        new(parameter.Name, parameter.Label, parameter.Kind.ToCode(), parameter.Min, parameter.Max,
            settings.Parameters.TryGetValue(parameter.Name, out var value) ? value : parameter.Default, parameter.Default, parameter.Description, parameter.Unit);
}
