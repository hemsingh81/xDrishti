using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Research;

/// <summary>
/// Puts the replays of the instruments of a universe together into the record of a run (spec P3-04 Scope 10): the totals, the trades and the end-of-data trades in one order, the exceptions of the calendar and the run key. Pure: the same
/// inputs give the same record and the same key, and nothing is read from a clock or a random source.
/// </summary>
public static class RunAssembler
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Assembles the run. <paramref name="instruments"/> are in run order, one per member of the universe, with the same ids. <c>Trades</c> and <c>OpenAtEnd</c> are ordered by entry instant, then by the instrument's place in the
    /// universe, then Buy before Sell; each trade carries the basket ids of the universe (none for a list).
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The replays are not one per member of the universe, in the order of its members.</exception>
    public static SimulationRun Assemble(RunHeader header, IReadOnlyList<InstrumentReplay> instruments)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(instruments);
        var members = header.Universe.Members;
        if (members.Count != instruments.Count)
        {
            throw new ArgumentException("The universe has " + members.Count + " members and " + instruments.Count + " replays were given.", nameof(instruments));
        }

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i].Id != instruments[i].Record.InstrumentId)
            {
                throw new ArgumentException("The replay at position " + i + " is for another instrument than the member of the universe at that place.", nameof(instruments));
            }
        }

        var records = instruments.Select(i => i.Record).ToList().AsReadOnly();
        var exceptions = CalendarExceptionsOf(header, records);
        var key = CanonicalText.Sha256Hex(KeyText(header, records, exceptions));
        IReadOnlyList<Guid> baskets = header.Universe.BasketId is { } basket ? [basket] : [];
        return new SimulationRun(
            SimulationRun.CurrentSchemaVersion,
            key,
            header.EngineVersion,
            header.Strategy,
            header.From,
            header.To,
            header.Options,
            header.AcknowledgeCsvAdjusted,
            header.Fills,
            header.Universe,
            exceptions,
            records,
            Merge(instruments, r => r.Trades, baskets),
            Merge(instruments, r => r.OpenAtEnd, baskets),
            TotalsOf(records));
    }

    /// <summary>
    /// The text whose SHA-256 is the run key: UTF-8 lines, each ending in <c>\n</c>: <c>xd-run &lt;schema&gt;</c>, <c>engine=</c>, <c>strategy=</c> (the definition hash), <c>params=</c>, <c>timeframe=</c>, <c>window=</c>, <c>options=</c>, <c>ack=</c>,
    /// <c>fills=</c> (the canonical JSON), <c>universe=</c> (the label), one <c>i=</c> per instrument in run order, and <c>cal=</c>. An <c>i=</c> line is <c>id|bars hash or -|skip reason or -|ex-date:kind:numerator:denominator,…|tick or -|1 if the tick is the fallback else 0</c>.
    /// Numbers are written by <see cref="CanonicalText"/>, so the text does not depend on the culture, on the scale of the tick or on the sign of a zero.
    /// </summary>
    public static string KeyText(RunHeader header, IReadOnlyList<InstrumentRun> instruments, IReadOnlyList<CalendarExceptionDay> calendarExceptions)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(instruments);
        ArgumentNullException.ThrowIfNull(calendarExceptions);
        var text = new StringBuilder();
        text.Append("xd-run ").Append(CanonicalText.Number(SimulationRun.CurrentSchemaVersion)).Append('\n');
        Line(text, "engine", header.EngineVersion);
        Line(text, "strategy", header.Strategy.DefinitionSha256);
        Line(text, "params", string.Join(',', header.Strategy.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + CanonicalText.Number(p.Value))));
        Line(text, "timeframe", header.Strategy.EvaluationTimeframe.ToCode());
        Line(text, "window", Date(header.From) + ".." + Date(header.To));
        Line(text, "options", CanonicalText.Number(header.Options.MinSessionCompleteness) + "," + CanonicalText.Number(header.Options.DiscontinuityThreshold));
        Line(text, "ack", header.AcknowledgeCsvAdjusted ? "1" : "0");
        Line(text, "fills", header.Fills);
        Line(text, "universe", header.Universe.Label);
        foreach (var instrument in instruments)
        {
            var actions = string.Join(',', instrument.Adjustments.Select(a => Date(a.ExDate) + ":" + a.Kind + ":" + CanonicalText.Number(a.Numerator) + ":" + CanonicalText.Number(a.Denominator)));
            Line(
                text,
                "i",
                instrument.InstrumentId.ToString("D") + "|" + (instrument.BarsSha256 ?? "-") + "|" + (instrument.Skipped is { } skip ? skip.ToCode() : "-") + "|" + actions + "|"
                + (instrument.TickSize is { } tick ? CanonicalText.Number(tick) : "-") + "|" + (instrument.TickFromFallback ? "1" : "0"));
        }

        Line(text, "cal", string.Join(',', calendarExceptions.Select(e => Date(e.Date) + ":" + (e.IsHoliday ? "holiday" : CanonicalText.Number(e.FromMinute) + "-" + CanonicalText.Number(e.ToMinute)))));
        return text.ToString();
    }

    private static void Line(StringBuilder text, string name, string value) => text.Append(name).Append('=').Append(value).Append('\n');

    private static string Date(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>The exceptions to the default week (a weekday holiday, a session other than the regular window, any session on a weekend) from the earliest first bar read to <c>To</c>: <c>SessionOf</c> asked for each date, because the calendar lists no days.</summary>
    private static ReadOnlyCollection<CalendarExceptionDay> CalendarExceptionsOf(RunHeader header, IReadOnlyList<InstrumentRun> instruments)
    {
        var exceptions = new List<CalendarExceptionDay>();
        var firsts = instruments.Where(i => i.FirstBar is not null).Select(i => i.FirstBar!.Value).ToList();
        if (firsts.Count > 0)
        {
            for (var date = firsts.Min(); date <= header.To; date = date.AddDays(1))
            {
                var weekday = date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
                var session = header.Calendar.SessionOf(date);
                if (session is null)
                {
                    if (weekday)
                    {
                        exceptions.Add(new CalendarExceptionDay(date, true, 0, 0));
                    }
                }
                else if (!weekday || !session.Value.IsRegular)
                {
                    exceptions.Add(new CalendarExceptionDay(date, false, session.Value.FromMinute, session.Value.ToMinute));
                }
            }
        }

        return exceptions.AsReadOnly();
    }

    private static ReadOnlyCollection<RunTrade> Merge(IReadOnlyList<InstrumentReplay> instruments, Func<InstrumentReplay, IReadOnlyList<SimTrade>> pick, IReadOnlyList<Guid> baskets)
    {
        var all = new List<(SimTrade Trade, int Place)>();
        for (var place = 0; place < instruments.Count; place++)
        {
            all.AddRange(pick(instruments[place]).Select(t => (t, place)));
        }

        return all.OrderBy(t => t.Trade.EntryEpochSeconds).ThenBy(t => t.Place).ThenBy(t => t.Trade.Side).Select(t => new RunTrade(t.Trade, baskets)).ToList().AsReadOnly();
    }

    private static RunCounters TotalsOf(ReadOnlyCollection<InstrumentRun> records)
    {
        var scans = new SortedDictionary<Side, ScanCounters>();
        var walk = WalkCounters.Empty;
        var fates = RunFates.Empty;
        var sessions = SessionCounts.Empty;
        long read = 0, fed = 0, excluded = 0, outside = 0, flagged = 0, candidates = 0, trades = 0;
        foreach (var record in records)
        {
            read += record.BarsRead;
            fed += record.BarsFed;
            excluded += record.BarsExcluded;
            outside += record.BarsOutsideSession;
            flagged += record.Discontinuities.Count;
            candidates += record.CandidatesInWindow;
            trades += record.TradesInWindow;
            sessions = sessions.Add(record.Sessions);
            walk = walk.Add(record.Walk);
            fates = fates.Add(record.Fates);
            foreach (var scan in record.Scans)
            {
                scans[scan.Side] = scans.TryGetValue(scan.Side, out var sum) ? sum.Add(scan) : scan;
            }
        }

        var skipped = records
            .Where(r => r.Skipped is not null)
            .GroupBy(r => r.Skipped!.Value)
            .OrderBy(g => g.Key)
            .Select(g => new SkipCount(g.Key, g.Count()))
            .ToList()
            .AsReadOnly();
        return new RunCounters(records.Count, records.Count(r => r.Skipped is null), skipped, read, fed, excluded, outside, sessions, flagged, scans.Values.ToList().AsReadOnly(), walk, fates, candidates, trades);
    }
}
