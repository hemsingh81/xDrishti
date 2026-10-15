using System.Globalization;
using System.Text;
using System.Xml;
using ExcelDataReader;
using ExcelDataReader.Exceptions;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// Reads the first sheet of a downloaded spreadsheet - legacy <c>.xls</c> (BIFF8) or <c>.xlsx</c> - into rows of text cells with ExcelDataReader (MIT, ADR 0029). Dhan's old <c>.xls</c> files use Windows code pages, which the runtime's code-page provider supplies. Text
/// is kept as it is, numbers are written with an invariant point, dates as <c>yyyy-MM-dd</c> (with the time when there is one), an empty cell as the empty text. A file that is not a readable spreadsheet (corrupt, password protected, not Excel at all) is
/// <c>portfolio.invalid</c> on <c>file</c>, never an exception (a package with broken XML or parts is such a file too); a sheet wider than <see cref="MaxColumns"/> columns or with more than <see cref="MaxCells"/> cells is refused before it is held in memory, and an <c>.xlsx</c> package that declares too many parts or too much unpacked data is refused before it is parsed (<see cref="SpreadsheetPackageGuard"/>, a decompression bomb); the content is never logged. The stream must be seekable (the report loader hands over a buffered file): any other stream is refused as unreadable, so the guard can never be skipped.
/// </summary>
internal sealed class SpreadsheetReader : ISpreadsheetReader
{
    /// <summary>The most columns a sheet may have: a sheet that declares thousands of them is not a report, and one row of it would be a large allocation.</summary>
    public const int MaxColumns = 256;

    /// <summary>The most cells (rows times columns) the reader keeps.</summary>
    public const int MaxCells = 500_000;

    /// <summary>The cell limit in force; tests lower it.</summary>
    internal int CellLimit { get; init; } = MaxCells;

    static SpreadsheetReader()
    {
        // Old .xls files carry their text in Windows code pages; they are not in .NET's default set.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public ValueTask<Result<IReadOnlyList<IReadOnlyList<string>>>> ReadAsync(Stream content, int maxRows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            if (!content.CanSeek)
            {
                // Fail closed: the guard needs to look at the package and come back, so a stream that cannot seek is not read at all rather than read without it.
                return Done(Result.Failure<IReadOnlyList<IReadOnlyList<string>>>(PortfolioErrors.Invalid("file", "The spreadsheet cannot be read. Download the report again as XLS, XLSX or CSV; a password-protected file cannot be read.")));
            }

            if (SpreadsheetPackageGuard.IsPackage(content) && SpreadsheetPackageGuard.Problem(content) is { } problem)
            {
                return Done(Result.Failure<IReadOnlyList<IReadOnlyList<string>>>(PortfolioErrors.Invalid("file", $"The spreadsheet is too large to read ({problem}). Download the report again with a shorter date range.")));
            }

            using var reader = ExcelReaderFactory.CreateReader(content, new ExcelReaderConfiguration { FallbackEncoding = Encoding.GetEncoding(1252), LeaveOpen = true });
            var rows = new List<IReadOnlyList<string>>();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (rows.Count >= maxRows)
                {
                    return Done(Result.Failure<IReadOnlyList<IReadOnlyList<string>>>(PortfolioErrors.Invalid("file", $"The spreadsheet has more than {maxRows:N0} rows. Split it by date and import the parts one after the other.")));
                }

                var width = reader.FieldCount;
                if (width > MaxColumns || (long)(rows.Count + 1) * width > CellLimit)
                {
                    return Done(Result.Failure<IReadOnlyList<IReadOnlyList<string>>>(PortfolioErrors.Invalid("file", $"The spreadsheet is too large (at most {MaxColumns} columns and {MaxCells:N0} cells). Download the report again with a shorter date range.")));
                }

                var cells = new string[width];
                for (var i = 0; i < cells.Length; i++)
                {
                    cells[i] = Text(reader.GetValue(i));
                }

                rows.Add(cells);
            }

            return Done(Result.Success<IReadOnlyList<IReadOnlyList<string>>>(rows));
        }
        catch (Exception ex) when (ex is ExcelReaderException or InvalidDataException or IOException or NotSupportedException or ArgumentException or InvalidOperationException or XmlException or FormatException or OverflowException or KeyNotFoundException or IndexOutOfRangeException)
        {
            return Done(Result.Failure<IReadOnlyList<IReadOnlyList<string>>>(PortfolioErrors.Invalid("file", "The spreadsheet cannot be read. Download the report again as XLS, XLSX or CSV; a password-protected file cannot be read.")));
        }
    }

    private static ValueTask<Result<IReadOnlyList<IReadOnlyList<string>>>> Done(Result<IReadOnlyList<IReadOnlyList<string>>> result) => ValueTask.FromResult(result);

    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        double number => number.ToString("0.############", CultureInfo.InvariantCulture),
        DateTime date => date.TimeOfDay == TimeSpan.Zero ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool flag => flag ? "TRUE" : "FALSE",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
