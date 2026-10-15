using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>The self-healing settings of the story (1, 5, 15 minutes; 2 failed days; 8 years), adjustable by a test.</summary>
internal sealed class FixedHealSettings : IHealSettings
{
    public IReadOnlyList<TimeSpan> RetryBackoff { get; set; } = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

    public int PersistDays { get; set; } = 2;

    public int DeepenMaxYears { get; set; } = 8;
}
