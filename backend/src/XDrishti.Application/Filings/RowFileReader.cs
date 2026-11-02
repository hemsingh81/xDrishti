using System.Text;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary>Reads one listed csv of the inbox as text (P15-04): through the source port, which re-verifies the hash at the end of the stream, bounded by the listed size, as strict UTF-8. The result is the text or a closed reject code, never the content.</summary>
internal static class RowFileReader
{
    private static readonly UTF8Encoding Strict = new(false, true);

    public static async ValueTask<(string? Text, string? Reject)> ReadAsync(IFilingsSource source, FilingEntry entry, CancellationToken cancellationToken)
    {
        var opened = await source.OpenAsync(entry.Ref, cancellationToken);
        if (opened.IsFailure)
        {
            return (null, opened.Error.Code);
        }

        try
        {
            await using var stream = opened.Value;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length > entry.SizeBytes)
            {
                return (null, FilingRejectReasons.ContentChanged);
            }

            return (Strict.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), null);
        }
        catch (FilingContentChangedException)
        {
            return (null, FilingRejectReasons.ContentChanged);
        }
        catch (DecoderFallbackException)
        {
            return (null, CalendarRejectReasons.BadEncoding);
        }
        catch (IOException)
        {
            return (null, FilingRejectReasons.Unreadable);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, FilingRejectReasons.Unreadable);
        }
    }
}
