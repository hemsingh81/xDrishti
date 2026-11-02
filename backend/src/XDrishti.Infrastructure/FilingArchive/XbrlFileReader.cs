using System.Text;
using System.Text.Unicode;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;
using XDrishti.Infrastructure.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// <see cref="IResultsXbrlReader"/> (P15-03, ADR 0184): bounds the file (<c>Filings:MaxFileMb</c>), refuses bytes that are not valid UTF-8 and any document type or entity declaration before an XML reader sees them, then hands the pure parser a reader made by <see cref="SafeXml"/>.
/// Every refusal is a closed reason on a rejected result. No network type is used anywhere in this class.
/// </summary>
internal sealed class XbrlFileReader(IOptions<FilingsOptions> options) : IResultsXbrlReader
{
    public async ValueTask<ParsedResults> ParseAsync(Stream file, DateOnly periodEnd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var max = options.Value.MaxFileMb * 1024L * 1024L;
        var (buffer, length) = await ReadBoundedAsync(file, max, cancellationToken);
        if (buffer is null)
        {
            return ParsedResults.Reject(ResultCodes.TooLarge);
        }

        if (length == 0)
        {
            return ParsedResults.Reject(ResultCodes.Empty);
        }

        var bytes = buffer.AsSpan(0, length);
        if (!Utf8.IsValid(bytes))
        {
            return ParsedResults.Reject(ResultCodes.InvalidEncoding);
        }

        if (bytes.IndexOf("<!DOCTYPE"u8) >= 0 || bytes.IndexOf("<!ENTITY"u8) >= 0)
        {
            return ParsedResults.Reject(ResultCodes.DtdProhibited);
        }

        using var text = new StreamReader(new MemoryStream(buffer, 0, length, writable: false), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);
        using var xml = SafeXml.Create(text, max);
        return ResultsXbrlParser.Parse(xml, periodEnd);
    }

    // Null buffer when the file is above the cap (nothing beyond the cap is kept).
    private static async ValueTask<(byte[]? Buffer, int Length)> ReadBoundedAsync(Stream file, long max, CancellationToken cancellationToken)
    {
        if (file.CanSeek && file.Length > max)
        {
            return (null, 0);
        }

        var capacity = (int)Math.Clamp(file.CanSeek ? file.Length + 1 : 81_920, 1, max + 1);
        var buffer = new byte[capacity];
        var length = 0;
        while (true)
        {
            if (length == buffer.Length)
            {
                if (length > max)
                {
                    return (null, 0);
                }

                Array.Resize(ref buffer, (int)Math.Min(buffer.Length * 2L, max + 1));
            }

            var read = await file.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
            {
                return length > max ? (null, 0) : (buffer, length);
            }

            length += read;
        }
    }
}
