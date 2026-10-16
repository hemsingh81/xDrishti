using System.Globalization;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Infrastructure.Portfolio;

internal sealed class HealSettings(IOptions<HealOptions> options) : IHealSettings
{
    private readonly IReadOnlyList<TimeSpan> _backoff = Parse(options.Value.RetryBackoffMinutes);

    public IReadOnlyList<TimeSpan> RetryBackoff => _backoff;

    public int PersistDays => options.Value.PersistDays;

    public int DeepenMaxYears => options.Value.DeepenMaxYears;

    public int AttentionRetentionDays => options.Value.AttentionRetentionDays;

    /// <summary>"1,5,15" as three waits; blank gives none. The options validation has checked the shape.</summary>
    internal static IReadOnlyList<TimeSpan> Parse(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(m => TimeSpan.FromMinutes(int.Parse(m, CultureInfo.InvariantCulture)))];
}
