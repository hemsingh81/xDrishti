namespace XDrishti.Api.Exports;

/// <summary>The caps of an export file (configuration section <c>Exports</c>). The configuration may lower them and never raise them: a value above the ceiling, or below 1, stops the host at start (<c>ValidateOnStart</c>).</summary>
internal sealed class ExportOptions
{
    /// <summary>The most rows of a file.</summary>
    public const int RowsCeiling = 100_000;

    /// <summary>The most bytes of a file.</summary>
    public const int BytesCeiling = 26_214_400;

    public int MaxCsvRows { get; set; } = RowsCeiling;

    public int MaxCsvBytes { get; set; } = BytesCeiling;

    /// <summary>True when both caps are from 1 up to their ceiling.</summary>
    public static bool IsValid(ExportOptions options) =>
        options is { MaxCsvRows: >= 1 and <= RowsCeiling, MaxCsvBytes: >= 1 and <= BytesCeiling };
}
