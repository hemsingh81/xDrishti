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

    public static readonly Error JobRunning = Error.Conflict(JobRunningCode, "A data update is already running.");

    public static readonly Error JobNotFound = Error.NotFound(JobNotFoundCode, "The data update does not exist.");

    public static readonly Error InstrumentNotFound = Error.NotFound(InstrumentNotFoundCode, "The instrument does not exist.");

    public static readonly Error MasterMissing = Error.Conflict(MasterMissingCode, "The instrument list is empty. Refresh the instrument list first, then load the CSV files.");

    public static Error Invalid(string field, string message)
    {
        var fields = new FieldErrors();
        fields.Add(field, message);
        return fields.ToError(InvalidCode, "Some values are not valid.");
    }
}
