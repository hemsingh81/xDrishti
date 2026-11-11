using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Selection.SizeScaling;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Sizing;

/// <summary>
/// The sizing study (BK-10, criteria 11 and 12): the closed trades of one stored run, each with the scale its share's day bars up to the day before its entry would have given, compared with the same trades unscaled. Read only: it writes nothing, places nothing, and
/// the scale is at most 1 (there is no up-scaling what-if). A trade whose share has too little history is taken at scale 1 and counted; a trade with no net R (a gross run) is left out and counted.
/// </summary>
internal sealed partial class GetSizingStudyHandler(IReportTradeSource trades, IBarQueries bars, ISizingStore store, ILogger<GetSizingStudyHandler> logger) : IQueryHandler<GetSizingStudyQuery, Result<SizingStudyView>>
{
    /// <summary>The most distinct shares one study reads bars for; a larger run is refused before any bar is read.</summary>
    internal const int MaxInstruments = 600;

    private static readonly double[] Sensitivities = [0.25, 0.5, 0.75];

    public async ValueTask<Result<SizingStudyView>> HandleAsync(GetSizingStudyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Run is not { } run || !RunKey().IsMatch(run))
        {
            return Invalid("run: the 64-character key of a stored run.");
        }

        if (query.From is { } from && query.To is { } to && from > to)
        {
            return Invalid("from is after to.");
        }

        var stored = (await store.CurrentAsync(cancellationToken)).Settings.Floor;
        if (query.Floor is { } asked && !Sensitivities.Contains(asked))
        {
            return Invalid("floor: 0.25, 0.5 or 0.75 (left out, it is the stored setting).");
        }

        var floor = query.Floor ?? stored;
        var options = new SizingOptions(Floor: floor);
        var closed = new List<ReportTrade>();
        var skipped = 0;
        await foreach (var t in trades.StreamTradesAsync([run], new ReportTradeFilter([], [], [], [], [], query.From, query.To), cancellationToken))
        {
            if (t.RNet is null)
            {
                skipped++;
            }
            else
            {
                closed.Add(t);
            }
        }

        var instruments = closed.Select(t => t.InstrumentId).Distinct().ToList();
        if (instruments.Count > MaxInstruments)
        {
            return Result.Failure<SizingStudyView>(Error.Validation("reports.too_large", $"The run has {instruments.Count} shares; a study reads at most {MaxInstruments}."));
        }

        var ordered = closed.OrderBy(t => t.EntryDate).ThenBy(t => t.Ordinal).ThenBy(t => t.TradeKey, StringComparer.Ordinal).ToList();
        var series = new Dictionary<Guid, IReadOnlyList<(DateOnly Date, double Close)>>();
        foreach (var id in instruments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = ordered.Where(t => t.InstrumentId == id).Min(t => t.EntryDate);
            var last = ordered.Where(t => t.InstrumentId == id).Max(t => t.EntryDate);
            var rows = await bars.ReadDailyBarsAsync(id, first.AddDays(-SizingInputSource.LookBackDays), last, cancellationToken);
            series[id] = [.. rows.Where(b => b.Complete).Select(b => (IndiaTime.Today(b.Ts), b.Close))];
        }

        var studyTrades = new List<StudyTrade>(ordered.Count);
        var withoutScale = 0;
        foreach (var t in ordered)
        {
            // Only closes before the entry date count: the previous close is the last one the decision could have seen.
            var returns = ReturnSeries.FromCloses(series[t.InstrumentId], t.EntryDate.AddDays(-1));
            var reading = VolTarget.Read([.. returns.Select(r => r.Value)], options);
            if (reading.Scale is not { } scale)
            {
                withoutScale++;
                scale = 1.0;
            }

            studyTrades.Add(new StudyTrade(t.RNet!.Value, scale));
        }

        SizingLog.Study(logger, ordered.Count, instruments.Count);
        var result = SizingStudy.Compare(studyTrades);
        return Result.Success(new SizingStudyView(run, floor, query.Floor is null ? "setting" : "sensitivity", withoutScale, skipped, result, SizingStudyResult.Caveat + "; recommends nothing"));
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex RunKey();

    private static Result<SizingStudyView> Invalid(string message) => Result.Failure<SizingStudyView>(Error.Validation("reports.invalid", message));
}
