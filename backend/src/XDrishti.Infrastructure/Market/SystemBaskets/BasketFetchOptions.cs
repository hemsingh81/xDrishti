using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Infrastructure.Market.SystemBaskets;

/// <summary>The settings <c>Baskets:System:Fetch:*</c> (BK-01), bounds checked at start-up. The fetch is OFF by default; nothing here is a secret.</summary>
internal sealed class BasketFetchOptions
{
    public const string SectionName = "Baskets:System:Fetch";

    /// <summary>Default false: with it off no HTTP client is registered and no request is ever made.</summary>
    public bool Enabled { get; set; }

    /// <summary>A descriptive name plus the owner's contact. Empty = the fetch refuses to run.</summary>
    [MaxLength(200)]
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>A or B: the file host asked first (default A).</summary>
    [RegularExpression("^[AB]$")]
    public string PreferredHost { get; set; } = "A";

    /// <summary>India time of day (HH:mm) from which a source is asked for the day.</summary>
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")]
    public string RunAt { get; set; } = "07:30";

    /// <summary>Minutes before each retry of a failed attempt.</summary>
    [Required]
    [MinLength(1)]
    [MaxLength(5)]
    public int[] BackoffMinutes { get; set; } = [5, 30, 120];

    [Range(0.0, 1.0)]
    public double MaxChangeFraction { get; set; } = 0.06;

    [Range(0, 100)]
    public int ChangeFloor { get; set; } = 3;

    [Range(0.0, 1.0)]
    public double TripwireFraction { get; set; } = 0.5;

    [Range(0.0, 1.0)]
    public double MaxUnmatchedFraction { get; set; } = 0.2;

    [Range(1, 30)]
    public int StaleAfterDays { get; set; } = 3;

    [Range(1, 168)]
    public int BackoffHours { get; set; } = 24;

    /// <summary>Months in which an index review ends (the window runs from <see cref="ReviewFromDay"/> of the month to <see cref="ReviewToDay"/> of the next).</summary>
    [Required]
    public int[] ReviewMonths { get; set; } = [3, 6, 9, 12];

    [Range(1, 28)]
    public int ReviewFromDay { get; set; } = 20;

    [Range(1, 28)]
    public int ReviewToDay { get; set; } = 7;
}

internal sealed class BasketFetchSettings(IOptions<BasketFetchOptions> options) : IBasketFetchSettings
{
    private BasketFetchOptions Value => options.Value;

    public bool Enabled => Value.Enabled;

    public string UserAgent => Value.UserAgent.Trim();

    public FetchLimits Limits => new((decimal)Value.MaxChangeFraction, Value.ChangeFloor, (decimal)Value.TripwireFraction, (decimal)Value.MaxUnmatchedFraction);

    public string PreferredHost => Value.PreferredHost;

    public TimeOnly RunAt => TimeOnly.ParseExact(Value.RunAt, "HH:mm", CultureInfo.InvariantCulture);

    public IReadOnlyList<int> BackoffMinutes => [.. Value.BackoffMinutes.Select(m => Math.Max(1, m))];

    public int StaleAfterDays => Value.StaleAfterDays;

    public int BackoffHours => Value.BackoffHours;

    public IReadOnlyList<int> ReviewMonths => Value.ReviewMonths;

    public int ReviewFromDay => Value.ReviewFromDay;

    public int ReviewToDay => Value.ReviewToDay;

    public string Snapshot() => JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["enabled"] = Enabled.ToString(CultureInfo.InvariantCulture),
        ["maxChangeFraction"] = Value.MaxChangeFraction.ToString(CultureInfo.InvariantCulture),
        ["changeFloor"] = Value.ChangeFloor.ToString(CultureInfo.InvariantCulture),
        ["tripwireFraction"] = Value.TripwireFraction.ToString(CultureInfo.InvariantCulture),
        ["maxUnmatchedFraction"] = Value.MaxUnmatchedFraction.ToString(CultureInfo.InvariantCulture),
    });
}
