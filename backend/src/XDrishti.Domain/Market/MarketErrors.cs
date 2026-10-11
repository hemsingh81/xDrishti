using XDrishti.Domain.Common;

namespace XDrishti.Domain.Market;

/// <summary>Stable error codes of the instrument master (part of the API contract).</summary>
public static class MarketErrors
{
    public const string RefreshRunningCode = "instruments.refresh_running";
    public const string NotFoundCode = "instruments.not_found";
    public const string InvalidCode = "instruments.invalid";

    public static readonly Error RefreshRunning = Error.Conflict(RefreshRunningCode, "A refresh of the instrument list is already running.");

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The instrument does not exist.");

    public static Error Invalid(string field, string message)
    {
        var fields = new FieldErrors();
        fields.Add(field, message);
        return fields.ToError(InvalidCode, "Some values are not valid.");
    }
}
