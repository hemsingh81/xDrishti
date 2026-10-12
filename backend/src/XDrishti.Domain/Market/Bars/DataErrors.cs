using XDrishti.Domain.Common;

namespace XDrishti.Domain.Market.Bars;

/// <summary>Stable error codes of the market data (part of the API contract).</summary>
public static class DataErrors
{
    public const string JobRunningCode = "data.job_running";
    public const string JobNotFoundCode = "data.job_not_found";
    public const string InstrumentNotFoundCode = "data.instrument_not_found";
    public const string InvalidCode = "data.invalid";
    public const string MasterMissingCode = "data.master_missing";
    public const string CsvFolderMissingCode = "data.csv_folder_missing";
    public const string DhanNotConnectedCode = "data.dhan_not_connected";
    public const string DhanNotAvailableCode = "data.dhan_not_available";
    public const string CalendarDayNotFoundCode = "data.calendar_day_not_found";
    public const string CorporateActionNotFoundCode = "data.corporate_action_not_found";
    public const string CorporateActionExistsCode = "data.corporate_action_exists";
    public const string CsvFileNotFoundCode = "data.csv_file_not_found";
    public const string CsvAlreadyAdjustedCode = "data.csv_already_adjusted";
    public const string CorporateActionConflictsSameDayCode = "data.corporate_action_conflicts_same_day";

    public static readonly Error JobRunning = Error.Conflict(JobRunningCode, "A data update is already running.");

    public static readonly Error JobNotFound = Error.NotFound(JobNotFoundCode, "The data update does not exist.");

    public static readonly Error InstrumentNotFound = Error.NotFound(InstrumentNotFoundCode, "The instrument does not exist.");

    public static readonly Error CalendarDayNotFound = Error.NotFound(CalendarDayNotFoundCode, "The date is not on the trading calendar.");

    public static readonly Error CorporateActionNotFound = Error.NotFound(CorporateActionNotFoundCode, "The corporate action does not exist.");

    public static readonly Error CorporateActionExists = Error.Conflict(CorporateActionExistsCode, "The instrument already has a corporate action of this kind on that ex-date.");

    public static readonly Error CorporateActionConflictsSameDay = Error.Conflict(
        CorporateActionConflictsSameDayCode,
        "The instrument already has an applied split or bonus on that ex-date. Ignore it first to apply another kind on the same day.");

    public static readonly Error CsvFileNotFound = Error.NotFound(CsvFileNotFoundCode, "That CSV file has not been loaded, so there is nothing to undo.");

    public static readonly Error MasterMissing = Error.Conflict(MasterMissingCode, "The instrument list is empty. Refresh the instrument list first, then load the CSV files.");

    /// <summary>
    /// The owner's CSV files are already adjusted at source. An action whose ex-date is on or before the date the loaded files reach would adjust those prices a second time, so it is refused until the
    /// owner says so (<c>acknowledgeCsvAdjusted</c>). <paramref name="actions"/> and <paramref name="instruments"/> are carried as extension members of the problem.
    /// </summary>
    public static Error CsvAlreadyAdjusted(int actions, int instruments, DateOnly? through = null) => Error.Conflict(
        CsvAlreadyAdjustedCode,
        $"The loaded CSV files are already adjusted for corporate actions up to {(through is { } date ? date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "the last bar they hold")}. Applying {(actions == 1 ? "an action" : $"{actions} actions")} with an ex-date on or before it would adjust those prices twice. Confirm that you want this, or leave it out.")
        with
    {
        Data = new Dictionary<string, int> { ["adjustsCsvTwice"] = actions, ["instruments"] = instruments },
    };

    public static Error Invalid(string field, string message)
    {
        var fields = new FieldErrors();
        fields.Add(field, message);
        return fields.ToError(InvalidCode, "Some values are not valid.");
    }
}
