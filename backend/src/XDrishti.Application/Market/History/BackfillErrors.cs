using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.History;

/// <summary>The closed list of refusals of the 1-minute backfill (CF-04 Part B). Codes are part of the contract.</summary>
public static class BackfillErrors
{
    public const string DisabledCode = "history.disabled";

    public const string DiskLowCode = "history.disk_low";

    public const string CalendarMissingCode = "history.calendar_missing";

    public const string NoTargetCode = "history.no_target";

    public static Error Disabled() => Error.Conflict(DisabledCode, "The 1-minute backfill is off (Data:History:Backfill:Enabled). Nothing was requested.");

    /// <summary>Free space is under the headroom times the estimate (or cannot be read): the run does not start.</summary>
    public static Error DiskLow(long? freeBytes, long requiredBytes, long rows) =>
        Error.Conflict(
            DiskLowCode,
            freeBytes is { } free
                ? string.Create(CultureInfo.InvariantCulture, $"Free space is {free:N0} bytes; the backfill of about {rows:N0} rows needs {requiredBytes:N0} (the estimate times the headroom). Nothing was requested.")
                : string.Create(CultureInfo.InvariantCulture, $"Free space cannot be read; the backfill of about {rows:N0} rows needs {requiredBytes:N0}. Nothing was requested."));

    /// <summary>Part A's daily files do not cover the range: the trading days of the range are not known (calendar before data).</summary>
    public static Error CalendarMissing(DateOnly from, DateOnly to, int missingDays) =>
        Error.Conflict(
            CalendarMissingCode,
            string.Create(CultureInfo.InvariantCulture, $"The daily files of Part A do not cover {from:yyyy-MM-dd} to {to:yyyy-MM-dd} ({missingDays} weekdays have no entry). Load them with 'xd data eod-load' first. Nothing was requested."));

    public static Error NoTarget() => Error.NotFound(NoTargetCode, "No tracked instrument with a Dhan id and stored CSV bars matches.");
}
