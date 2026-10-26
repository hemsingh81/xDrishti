using Microsoft.Extensions.Configuration;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Paper;

namespace XDrishti.Infrastructure.Paper;

/// <summary>The flattened <c>Planning:*</c> configuration section (spec P10-05): the settings of the plan builder, the open book, grading and the ticket alerts, read from the configuration of this process. Keys lower-cased with <c>:</c> as the separator.</summary>
internal sealed class PlanningSettingsReader(IConfiguration configuration) : IPlanningSettingsReader
{
    public IReadOnlyDictionary<string, string> Read() =>
        configuration.GetSection("Planning").AsEnumerable()
            .Where(p => p.Key.StartsWith("Planning:", StringComparison.OrdinalIgnoreCase) && p.Value is not null)
            .Where(p => !PaperConfigKeys.NotificationPlanningSections.Any(n => p.Key.StartsWith("Planning:" + n + ":", StringComparison.OrdinalIgnoreCase))) // notification choices are not part of the measured configuration
            .ToDictionary(p => p.Key["Planning:".Length..].ToLowerInvariant(), p => p.Value!, StringComparer.Ordinal);
}
