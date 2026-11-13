namespace XDrishti.Domain.Market.Context;

/// <summary>The closed reject codes of the context CSV loader.</summary>
public static class ContextCsvCodes
{
    public const string NoHeader = "no_header";
    public const string MissingColumn = "missing_column";
    public const string WrongIndex = "wrong_index";
    public const string ShortRow = "short_row";
    public const string BadDate = "bad_date";
    public const string BadNumber = "bad_number";
    public const string BadRange = "bad_range";
    public const string DuplicateDate = "duplicate_date";
    public const string NotASession = "not_a_session";
    public const string NotFinal = "not_final";
    public const string Unreadable = "unreadable";
    public const string Empty = "empty";
}
