using System.Globalization;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary>
/// <c>GET /api/filings</c> (spec P15-02 criterion 11): envelope metadata, newest broadcast first, at most 200 rows, no file content and no path. With <c>asOf</c> (an instant with an offset; needs an instrument and a kind) the list is the point-in-time answer of
/// <see cref="IPointInTimeFilings"/>. An unknown kind or instrument, a bad <c>asOf</c> or an <c>asOf</c> without an offset is <c>filings.invalid_query</c>; nothing stored is an empty list.
/// </summary>
internal sealed class ListFilingsHandler(IFilingReader reader, IFilingInstruments instruments, IPointInTimeFilings pointInTime) : IQueryHandler<ListFilingsQuery, Result<FilingListView>>
{
    public const int MaxRows = 200;

    private static readonly DateTimeOffset EarliestAsOf = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] AsOfFormats = ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mmK"];

    public async ValueTask<Result<FilingListView>> HandleAsync(ListFilingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        FilingKind? kind = null;
        if (!string.IsNullOrWhiteSpace(query.Kind))
        {
            if (!FilingKinds.TryParse(query.Kind.Trim(), out var parsed))
            {
                return Fail("Unknown kind: results, shareholding, board_meeting or announcement.");
            }

            kind = parsed;
        }

        Guid? instrumentId = null;
        if (!string.IsNullOrWhiteSpace(query.Instrument))
        {
            var match = await instruments.ResolveAsync(query.Instrument.Trim().ToUpperInvariant(), cancellationToken);
            if (match.Id is not { } id)
            {
                return Fail("Unknown instrument.");
            }

            instrumentId = id;
        }

        if (query.AsOf is null)
        {
            var rows = await reader.ListAsync(instrumentId, kind, MaxRows + 1, cancellationToken);
            return Result.Success(new FilingListView(null, 0, [.. rows.Take(MaxRows)], rows.Count > MaxRows));
        }

        if (!TryParseAsOf(query.AsOf, out var t))
        {
            return Fail("asOf must be an instant with an offset, such as 2026-07-24T16:17:00+05:30 or 2026-07-24T10:47:00Z.");
        }

        if (instrumentId is null || kind is null)
        {
            return Fail("asOf needs an instrument and a kind.");
        }

        if (t < EarliestAsOf)
        {
            return Fail("asOf must not be before 2000-01-01.");
        }

        var answer = await pointInTime.AsOf(instrumentId.Value, kind.Value, t, cancellationToken);
        var newestFirst = answer.Filings.OrderByDescending(f => f.BroadcastTs).ThenBy(f => f.Id).Take(MaxRows + 1).ToList();
        var views = (await reader.ByIdsAsync([.. newestFirst.Select(f => f.Id)], cancellationToken)).ToDictionary(v => v.Id);
        var ordered = newestFirst.Where(f => views.ContainsKey(f.Id)).Select(f => views[f.Id]).ToList();
        return Result.Success(new FilingListView(answer.State, answer.UnavailableCount, [.. ordered.Take(MaxRows)], answer.Filings.Count > MaxRows));
    }

    private static Result<FilingListView> Fail(string message) => Result.Failure<FilingListView>(FilingErrors.InvalidQuery(message));

    private static bool TryParseAsOf(string text, out DateTimeOffset t) =>
        DateTimeOffset.TryParseExact(text.Trim(), AsOfFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && HasOffset(text.Trim());

    // K also accepts nothing at all (a local time): demand a Z or a +hh:mm / -hh:mm at the end.
    private static bool HasOffset(string text) => text.EndsWith('Z') || (text.Length > 6 && (text[^6] is '+' or '-') && text[^3] == ':');
}
