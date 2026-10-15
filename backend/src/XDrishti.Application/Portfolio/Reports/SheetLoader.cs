using System.Text;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>
/// Turns the bytes of an uploaded report into a <see cref="Sheet"/> (ADR 0029). The format is judged from the first bytes, not from the file name: a legacy spreadsheet starts with the compound-document signature, an <c>.xlsx</c> file is a zip archive
/// (<c>PK</c>), anything else is read as UTF-8 comma separated text (fields in double quotes when they contain commas or line breaks, the same rules as the tradebook CSV). Spreadsheets are read by the <see cref="ISpreadsheetReader"/> port. At most
/// <see cref="ReportLimits.MaxRows"/> rows and <see cref="TradeCsvLimits.MaxBytes"/> bytes. Nothing of the content is logged.
/// </summary>
internal static class SheetLoader
{
    private static readonly byte[] CompoundDocument = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

    public static async ValueTask<Result<Sheet>> LoadAsync(TradeFileContent content, ISpreadsheetReader spreadsheets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(spreadsheets);
        if (content.Length > TradeCsvLimits.MaxBytes)
        {
            return Result.Failure<Sheet>(PortfolioErrors.Invalid("file", $"The file is larger than {TradeCsvLimits.MaxBytes / (1024 * 1024)} MB."));
        }

        if (content.Length == 0)
        {
            return Result.Failure<Sheet>(PortfolioErrors.Invalid("file", "The file is empty."));
        }

        await using var stream = content.Open();
        var head = new byte[CompoundDocument.Length];
        var read = await stream.ReadAsync(head, cancellationToken);
        stream.Position = 0;
        if (head.AsSpan(0, Math.Min(read, CompoundDocument.Length)).StartsWith(CompoundDocument) || head.AsSpan(0, Math.Min(read, Zip.Length)).StartsWith(Zip))
        {
            var rows = await spreadsheets.ReadAsync(stream, ReportLimits.MaxRows, cancellationToken);
            return rows.IsFailure
                ? Result.Failure<Sheet>(rows.Error)
                : Result.Success(new Sheet([.. rows.Value.Select((cells, i) => new SheetRow(i + 1, cells))]));
        }

        return await ReadTextAsync(stream, cancellationToken);
    }

    private static async ValueTask<Result<Sheet>> ReadTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(stream, StrictUtf8, detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true);
            var lines = new LineReader(reader);
            var rows = new List<SheetRow>();
            while (await lines.NextAsync(cancellationToken) is { } record)
            {
                if (rows.Count >= ReportLimits.MaxRows)
                {
                    return Result.Failure<Sheet>(PortfolioErrors.Invalid("file", $"The file has more than {TradeCsvLimits.MaxRows:N0} data rows. Split it by date and import the parts one after the other."));
                }

                rows.Add(new SheetRow(record.Line, record.Fields));
            }

            return Result.Success(new Sheet(rows));
        }
        catch (DecoderFallbackException)
        {
            return Result.Failure<Sheet>(PortfolioErrors.Invalid("file", "The file is not valid UTF-8 text. Download the report again as CSV, XLS or XLSX."));
        }
    }
}

/// <summary>The limits of a downloaded report: the data rows of the tradebook CSV plus a margin for the title, the personal lines, the header and the footer.</summary>
internal static class ReportLimits
{
    public const int MaxRows = TradeCsvLimits.MaxRows + 64;
}
