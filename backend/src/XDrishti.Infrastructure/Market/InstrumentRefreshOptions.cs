using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Market;

/// <summary>Automatic refresh of the instrument master (configuration section <c>Instruments:AutoRefresh</c>).</summary>
internal sealed class InstrumentRefreshOptions
{
    public const string SectionName = "Instruments:AutoRefresh";

    public const string TimeFormat = "HH:mm";

    public const string InvalidTimeMessage = "Instruments:AutoRefresh:TimeIst must be a time of day as HH:mm (24-hour), India time, e.g. 08:45.";

    /// <summary>Whether the API refreshes the master list by itself every day.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Time of day, India time (UTC+05:30), as <c>HH:mm</c>. Dhan publishes the file around 08:20 IST.</summary>
    [Required]
    public string TimeIst { get; set; } = "08:45";

    public static bool IsValidTime(string? value) =>
        TimeOnly.TryParseExact(value, TimeFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
}
