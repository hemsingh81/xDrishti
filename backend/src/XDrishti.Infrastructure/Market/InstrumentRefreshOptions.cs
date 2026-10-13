using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Market;

/// <summary>First-start settings of the instrument master refresh service (configuration section <c>Instruments:AutoRefresh</c>, ADR 0018): read once, when the service has no stored row.</summary>
internal sealed class InstrumentRefreshOptions
{
    public const string SectionName = "Instruments:AutoRefresh";

    public const string TimeFormat = "HH:mm";

    public const string InvalidTimeMessage = "Instruments:AutoRefresh:TimeIst must be a time of day as HH:mm (24-hour), India time, e.g. 08:45.";

    /// <summary>Whether the service starts switched on (first start only).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Time of day, India time (UTC+05:30), as <c>HH:mm</c>. Dhan publishes the file around 08:20 IST.</summary>
    [Required]
    public string TimeIst { get; set; } = "08:45";

    public static bool IsValidTime(string? value) =>
        TimeOnly.TryParseExact(value, TimeFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
}
