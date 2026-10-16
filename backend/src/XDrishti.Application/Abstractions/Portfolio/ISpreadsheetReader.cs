using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// Reads a spreadsheet file the owner downloaded (legacy <c>.xls</c> BIFF8 or <c>.xlsx</c>; ADR 0029) into rows of text cells, so that the report readers of the application stay pure and work on the same shape for every file format. The first sheet is read; a number is
/// written with an invariant point as the decimal separator, a date as <c>yyyy-MM-dd</c> (with the time when it has one), an empty cell as the empty text. A file that is not a readable spreadsheet is <c>portfolio.invalid</c> on field <c>file</c>, never an exception.
/// </summary>
public interface ISpreadsheetReader
{
    /// <summary>The rows of the first sheet, at most <paramref name="maxRows"/> of them (more is <c>portfolio.invalid</c>).</summary>
    ValueTask<Result<IReadOnlyList<IReadOnlyList<string>>>> ReadAsync(Stream content, int maxRows, CancellationToken cancellationToken);
}
