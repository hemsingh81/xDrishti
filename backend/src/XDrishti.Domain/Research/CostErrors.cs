using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>Stable error codes of the charge sheet, the model and the sizing (P3-03c), and the codes of the field errors a sheet can have. Every <c>costs.*</c> code is declared here once.</summary>
public static class CostErrors
{
    /// <summary>The sheet is not valid; the fields carry the path and the code of each problem (at most <see cref="MaxFieldErrors"/>).</summary>
    public const string SheetInvalidCode = "costs.sheet_invalid";

    /// <summary>The text of a sheet is longer than allowed (<see cref="MaxInputLength"/> before parsing, <see cref="MaxCanonicalLength"/> canonical).</summary>
    public const string SheetTooLongCode = "costs.sheet_too_long";

    /// <summary>A date before the first schedule of the sheet: the sheet has no rates for it. A caller error in the replay.</summary>
    public const string NoRatesForDateCode = "costs.no_rates_for_date";

    /// <summary>A capital, entry or risk that is not above 0.</summary>
    public const string SizingInputInvalidCode = "costs.sizing_input_invalid";

    /// <summary>No sheet is stored under the id (P3-03f), or the id is a <c>test-</c> one, which is never stored.</summary>
    public const string SheetNotFoundCode = "costs.sheet_not_found";

    /// <summary>A <c>test-</c> sheet is synthetic and is never stored (P3-03f).</summary>
    public const string TestSheetNotStoredCode = "costs.test_sheet_not_stored";

    /// <summary>A stored sheet has other rates and the load was not told to replace them (P3-03f).</summary>
    public const string ReplaceRequiredCode = "costs.replace_required";

    /// <summary>Another write to the same sheet landed first (P3-03f).</summary>
    public const string ConcurrentWriteCode = "costs.concurrent_write";

    /// <summary>The most field errors an invalid sheet lists.</summary>
    public const int MaxFieldErrors = 20;

    /// <summary>The longest text <see cref="CostSheetJson.Parse"/> reads, in characters.</summary>
    public const int MaxInputLength = 16_000;

    /// <summary>The longest canonical text of a sheet, in characters.</summary>
    public const int MaxCanonicalLength = 8_000;

    /// <summary>A field is missing or null.</summary>
    public const string Required = "required";

    /// <summary>A number was expected: a string, a boolean, an object, NaN or an infinity was found.</summary>
    public const string NotANumber = "not_a_number";

    /// <summary>A value below 0.</summary>
    public const string Negative = "negative";

    /// <summary>A value above its bound (a net against a slipped unit).</summary>
    public const string TooLarge = "too_large";

    /// <summary>A rate with more than 6 decimal places.</summary>
    public const string TooPrecise = "too_precise";

    /// <summary>The effective dates are not strictly ascending.</summary>
    public const string NotAscending = "not_ascending";

    /// <summary>Two schedules have one effective date.</summary>
    public const string Duplicate = "duplicate";

    /// <summary>A property name repeated in one object.</summary>
    public const string DuplicateProperty = "duplicate_property";

    /// <summary>A brokerage cap of 0 with a percent or a minimum above 0.</summary>
    public const string BrokerageCapRequired = "brokerage_cap_required";

    /// <summary>A brokerage minimum above a non-zero cap.</summary>
    public const string MinAboveCap = "min_above_cap";

    /// <summary>A field the sheet does not have.</summary>
    public const string UnknownField = "unknown_field";

    /// <summary>The text is not JSON.</summary>
    public const string InvalidJson = "invalid_json";

    /// <summary>A value of the wrong kind or outside its list (an id outside the pattern, an unknown rounding, a date that is not <c>yyyy-MM-dd</c>).</summary>
    public const string InvalidValue = "invalid_value";

    /// <summary>A date outside 2000 to 2099, or a count of schedules outside 1 to 12.</summary>
    public const string OutOfRange = "out_of_range";

    /// <summary>A text longer than its bound.</summary>
    public const string TooLong = "too_long";

    /// <summary>The sheet is invalid for these field problems (the first <see cref="MaxFieldErrors"/> are kept).</summary>
    public static Error SheetInvalid(IReadOnlyList<FieldError> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Error.Validation(SheetInvalidCode, "The charge sheet is not valid.") with { Fields = [.. fields.Take(MaxFieldErrors)] };
    }

    /// <summary>The sheet is invalid for one problem.</summary>
    public static Error SheetInvalid(string path, string code, string message) => SheetInvalid([new FieldError(path, message, null, code)]);

    /// <summary>No sheet with the id is stored.</summary>
    public static Error SheetNotFound(string sheetId) =>
        Error.NotFound(SheetNotFoundCode, $"No charge sheet '{sheetId}' is stored.");

    /// <summary>A text over its bound.</summary>
    public static Error SheetTooLong(string what, int length, int maximum) =>
        Error.Validation(SheetTooLongCode, $"The {what} of the charge sheet is {length} characters; the most allowed is {maximum}.");

    /// <summary>The date is before the first schedule.</summary>
    public static Error NoRatesForDate(string sheetId, DateOnly date) =>
        Error.Validation(NoRatesForDateCode, $"The charge sheet '{sheetId}' has no rates for {date:yyyy-MM-dd}: its first schedule starts later.");

    /// <summary>A sizing input that is not above 0.</summary>
    public static Error SizingInputInvalid(string field) =>
        Error.Validation(SizingInputInvalidCode, $"The sizing input '{field}' is not above 0 or is above its bound.");
}
