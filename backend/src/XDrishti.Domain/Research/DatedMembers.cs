using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Market;

namespace XDrishti.Domain.Research;

/// <summary>
/// The membership of a basket over time as intervals, read once per run (BK-11b): an instrument is a member on a day when it was added before the end of that day and not removed before it, as the members read says ("a member at the end of that day").
/// Pure lookup: no clock, no port.
/// </summary>
public sealed class DatedMembers
{
    private readonly Dictionary<Guid, List<(DateTimeOffset From, DateTimeOffset? To)>> _byInstrument = [];
    private readonly List<(Guid Id, DateTimeOffset From, DateTimeOffset? To)> _all;

    /// <summary>Builds the lookup from the stays of a basket.</summary>
    /// <param name="stays">One row per stay: the instrument, when it was added and when it was removed (null while it is a member).</param>
    public DatedMembers(IEnumerable<(Guid InstrumentId, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo)> stays)
    {
        ArgumentNullException.ThrowIfNull(stays);
        _all = [.. stays.Select(s => (s.InstrumentId, s.ValidFrom, s.ValidTo))];
        foreach (var (id, from, to) in _all)
        {
            if (!_byInstrument.TryGetValue(id, out var list))
            {
                _byInstrument[id] = list = [];
            }

            list.Add((from, to));
        }
    }

    /// <summary>Every instrument that has a stay, ordered by id.</summary>
    public IReadOnlyList<Guid> Instruments => [.. _byInstrument.Keys.Order()];

    /// <summary>The India days on which membership changed (an addition or a removal), ascending.</summary>
    public IReadOnlyList<DateOnly> ChangeDays => [.. _all.SelectMany(s => s.To is { } to ? new[] { IndiaTime.Today(s.From), IndiaTime.Today(to) } : [IndiaTime.Today(s.From)]).Distinct().Order()];

    /// <summary>Whether the instrument is a member at the end of <paramref name="day"/>.</summary>
    public bool IsMember(Guid instrumentId, DateOnly day)
    {
        if (!_byInstrument.TryGetValue(instrumentId, out var stays))
        {
            return false;
        }

        var cutoff = Cutoff(day);
        foreach (var (from, to) in stays)
        {
            if (from < cutoff && (to is null || to >= cutoff))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the instrument is a member at the end of at least one day from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public bool IsMemberDuring(Guid instrumentId, DateOnly from, DateOnly to)
    {
        if (!_byInstrument.TryGetValue(instrumentId, out var stays))
        {
            return false;
        }

        foreach (var (start, end) in stays)
        {
            var first = IndiaTime.Today(start);
            var last = end is { } removed ? IndiaTime.Today(removed).AddDays(-1) : DateOnly.MaxValue;
            if (first <= to && last >= from && first <= last)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The members at the end of <paramref name="day"/>, ordered by id.</summary>
    public IReadOnlyList<Guid> On(DateOnly day) => [.. _byInstrument.Keys.Where(id => IsMember(id, day)).Order()];

    /// <summary>The lookup of one instrument: the day test the walker uses.</summary>
    public Func<DateOnly, bool> Of(Guid instrumentId) => day => IsMember(instrumentId, day);

    /// <summary>
    /// Whether the history can stand for a window that starts on <paramref name="from"/>: at least two days of change (the rule of ADR 0263) and the first of them on or before the window start. When it cannot, the first day of change is given back for the refusal.
    /// </summary>
    public bool IsDatedFor(DateOnly from, out DateOnly? firstDay)
    {
        var days = ChangeDays;
        firstDay = days.Count == 0 ? null : days[0];
        return days.Count >= 2 && days[0] <= from;
    }

    /// <summary>
    /// The lower-case hex SHA-256 of the stays that touch the window, clipped to it and ordered (instrument, first day, last day): two runs over different membership histories hash differently. A stay that is still open ends on <paramref name="to"/>.
    /// </summary>
    public string Hash(DateOnly from, DateOnly to)
    {
        var rows = new List<(Guid Id, DateOnly First, DateOnly Last)>();
        foreach (var (id, start, end) in _all)
        {
            var first = IndiaTime.Today(start);
            var last = end is { } removed ? IndiaTime.Today(removed).AddDays(-1) : to;   // removed at the start of a day: a member until the day before
            if (first < from)
            {
                first = from;
            }

            if (last > to)
            {
                last = to;
            }

            if (first <= last)
            {
                rows.Add((id, first, last));
            }
        }

        var text = new StringBuilder();
        foreach (var (id, first, last) in rows.OrderBy(r => r.Id).ThenBy(r => r.First).ThenBy(r => r.Last))
        {
            text.Append(id.ToString("D")).Append('|').Append(first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|').Append(last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static DateTimeOffset Cutoff(DateOnly day) => new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime();
}
