using XDrishti.Domain.Common;

namespace XDrishti.Domain.Market;

/// <summary>Stable error codes of the instrument master (part of the API contract).</summary>
public static class MarketErrors
{
    public const string RefreshRunningCode = "instruments.refresh_running";
    public const string NotFoundCode = "instruments.not_found";
    public const string InvalidCode = "instruments.invalid";
    public const string RetiredCode = "instruments.retired";
    public const string TrackingCollidedCode = "instruments.tracking_collided";

    public static readonly Error RefreshRunning = Error.Conflict(RefreshRunningCode, "A refresh of the instrument list is already running.");

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The instrument does not exist.");

    public static readonly Error TrackingCollided = Error.Conflict(TrackingCollidedCode, "The tracked list was changed at the same time. Try again.");

    /// <summary>Retired instruments cannot be newly tracked (they are no longer in Dhan's list); names the first few.</summary>
    public static Error Retired(IReadOnlyList<string> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var shown = string.Join(", ", symbols.Take(5));
        var more = symbols.Count > 5 ? $" and {symbols.Count - 5} more" : string.Empty;
        var fields = new FieldErrors();
        fields.Add("instrumentIds", $"Not in Dhan's list any more, so it cannot be tracked: {shown}{more}.");
        return fields.ToError(RetiredCode, "Retired instruments cannot be tracked.");
    }

    public static Error Invalid(string field, string message)
    {
        var fields = new FieldErrors();
        fields.Add(field, message);
        return fields.ToError(InvalidCode, "Some values are not valid.");
    }
}
