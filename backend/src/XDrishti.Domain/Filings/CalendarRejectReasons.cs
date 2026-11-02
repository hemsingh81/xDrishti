namespace XDrishti.Domain.Filings;

/// <summary>The closed list of why a row, a group or a whole csv of the calendar and shareholding loaders is not loaded (P15-04). Text only from this list is ever logged.</summary>
public static class CalendarRejectReasons
{
    public const string BadHeader = "bad_header";
    public const string BadEncoding = "bad_encoding";
    public const string TooManyRows = "too_many_rows";
    public const string BadRow = "bad_row";
    public const string UnknownInstrument = "unknown_instrument";
    public const string AmbiguousSymbol = "ambiguous_symbol";
    public const string InconsistentTs = "inconsistent_ts";
    public const string OutOfRange = "out_of_range";
    public const string AmbiguousOrder = "ambiguous_order";
    public const string TooLong = "too_long";
}
