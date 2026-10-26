namespace XDrishti.SimExport.Csv;

/// <summary>What a cell of an export holds (P8-20): the kind decides how it is written, so a number is only ever produced from a numeric type and a text is always defused.</summary>
public enum ExportCellKind
{
    /// <summary>Nothing: an empty cell (never a zero, a dash or a word standing for a number).</summary>
    Empty,

    /// <summary>Free text: control and format characters are dropped, a formula start is defused, RFC 4180 quoting.</summary>
    Text,

    /// <summary>A whole number.</summary>
    Whole,

    /// <summary>A real number in the invariant spelling.</summary>
    Real,

    /// <summary>A calendar date, <c>yyyy-MM-dd</c>.</summary>
    Date,

    /// <summary>true or false.</summary>
    Flag,
}

/// <summary>One cell of an export table. Built only through the factories, from typed values; a text can never become a number here.</summary>
public readonly record struct ExportCell
{
    private readonly string? _text;
    private readonly long _whole;
    private readonly double _real;
    private readonly DateOnly _date;

    private ExportCell(ExportCellKind kind, string? text = null, long whole = 0, double real = 0, DateOnly date = default)
    {
        Kind = kind;
        _text = text;
        _whole = whole;
        _real = real;
        _date = date;
    }

    /// <summary>What the cell holds.</summary>
    public ExportCellKind Kind { get; }

    /// <summary>The text of a text cell; null for any other kind.</summary>
    public string? Text => _text;

    /// <summary>An empty cell.</summary>
    public static ExportCell Empty => default;

    /// <summary>A text cell; null or empty text is an empty cell.</summary>
    public static ExportCell OfText(string? text) => string.IsNullOrEmpty(text) ? default : new(ExportCellKind.Text, text);

    /// <summary>A whole number.</summary>
    public static ExportCell OfWhole(long value) => new(ExportCellKind.Whole, whole: value);

    /// <summary>A real number; NaN and infinity are not numbers of a table and become an empty cell.</summary>
    public static ExportCell OfReal(double value) => double.IsFinite(value) ? new(ExportCellKind.Real, real: value) : default;

    /// <summary>A date.</summary>
    public static ExportCell OfDate(DateOnly value) => new(ExportCellKind.Date, date: value);

    /// <summary>A true or false word.</summary>
    public static ExportCell OfFlag(bool value) => new(ExportCellKind.Flag, whole: value ? 1 : 0);

    internal void WriteTo(CsvRow row)
    {
        switch (Kind)
        {
            case ExportCellKind.Text:
                row.Text(_text);
                break;
            case ExportCellKind.Whole:
                row.Number(_whole);
                break;
            case ExportCellKind.Real:
                row.Number(_real);
                break;
            case ExportCellKind.Date:
                row.Date(_date);
                break;
            case ExportCellKind.Flag:
                row.Bool(_whole != 0);
                break;
            default:
                row.Empty();
                break;
        }
    }
}
