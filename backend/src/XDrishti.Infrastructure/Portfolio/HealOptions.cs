using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// Deployment settings of the self-healing syncs (configuration section <c>Portfolio:Heal</c>, P2-16, ADR 0027). The defaults are the ones of the story and need no configuration; the keys exist to change them without a code change (environment variables
/// <c>Portfolio__Heal__RetryBackoffMinutes</c> and so on).
/// </summary>
internal sealed class HealOptions
{
    public const string SectionName = "Portfolio:Heal";

    /// <summary>
    /// The waits before each quiet retry of a sync that failed for a reason that may pass (Dhan did not answer, a rate limit), in minutes, comma-separated; their number is the number of retries of one occurrence, all inside the day. Default <c>1,5,15</c>: the first retry a minute
    /// after the failure, the second five minutes after the second failure, the third fifteen minutes after the third. Empty switches the retries off.
    /// </summary>
    [RegularExpression(@"^\s*(\d{1,3}(\s*,\s*\d{1,3}){0,9})?\s*$", ErrorMessage = "Up to ten waits of 1 to 999 minutes, separated by commas.")]
    public string RetryBackoffMinutes { get; set; } = "1,5,15";

    /// <summary>The consecutive India days whose last attempt failed after which a failing sync becomes an item in the bell (default 2). What only the owner can fix (the Dhan login) is told at once.</summary>
    [Range(1, 30)]
    public int PersistDays { get; set; } = 2;

    /// <summary>How many years back from today the nightly deepening of the trade history reads at most, one year a night (default 8); 0 switches it off.</summary>
    [Range(0, 30)]
    public int DeepenMaxYears { get; set; } = 8;

    /// <summary>How many days a resolved item of the bell is kept before the healer tick deletes it (default 30, ADR 0030). Unresolved items are never deleted.</summary>
    [Range(1, 3650)]
    public int AttentionRetentionDays { get; set; } = 30;
}
