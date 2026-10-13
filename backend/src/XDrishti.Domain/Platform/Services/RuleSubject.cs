namespace XDrishti.Domain.Platform.Services;

/// <summary>A registered service with the settings it would run on (stored or default, or a candidate being checked): one member of the set <see cref="ScheduleRules"/> looks at.</summary>
public sealed record RuleSubject(ServiceDefinition Definition, ServiceSettings Settings);
