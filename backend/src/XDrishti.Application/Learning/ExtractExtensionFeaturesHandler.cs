using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Learning;

/// <summary>
/// Extracts the extension features <c>ev-1</c> or <c>fu-1</c> of the trades of a stored run (spec P15-05, ADR 0185): reads the run's trades (decision instant only), the results calendar, the results filing times and, for <c>ev-1</c>, the stored NIFTY 50 closes once, then one instrument at a time through
/// <see cref="ExtensionFeatureExtractor"/>, writes the rows through <see cref="IExtensionFeatureStore"/> (insert-only, idempotent) and one record of what they were built from. A second call finds the record and stores nothing. Research only: no decision path reads the rows.
/// </summary>
internal sealed class ExtractExtensionFeaturesHandler(
    IRunStore runs,
    ITradingCalendar calendars,
    IEventCalendarSnapshots snapshots,
    IResultsFilingTimes filingTimes,
    IContextDayStore contextDays,
    IFilingSettings settings,
    ExtensionFeatureExtractor extractor,
    IExtensionFeatureStore rowsStore,
    IExtensionExtractionStore records,
    TimeProvider time,
    ILogger<ExtractExtensionFeaturesHandler> logger) : ICommandHandler<ExtractExtensionFeaturesCommand, ExtensionFeaturesOutcome>
{
    public async ValueTask<Result<ExtensionFeaturesOutcome>> HandleAsync(ExtractExtensionFeaturesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var group = ExtensionCatalogs.GroupOf(command.Version);
        var problems = new List<FieldError>();
        if (!RunRecordText.IsHex64(command.RunKey))
        {
            problems.Add(new FieldError("runKey", "A run key is 64 lower-case hex characters."));
        }

        if (group is null)
        {
            problems.Add(new FieldError("extension", "The extension is ev-1 or fu-1."));
        }

        if (problems.Count > 0 || group is null)
        {
            ExtensionFeatureLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<ExtensionFeaturesOutcome>(LearningErrors.Invalid(problems));
        }

        if (await runs.FindAsync(command.RunKey, cancellationToken) is null)
        {
            ExtensionFeatureLog.Refused(logger, LearningErrors.RunNotFoundCode);
            return Result.Failure<ExtensionFeaturesOutcome>(LearningErrors.RunNotFound(command.RunKey));
        }

        var started = time.GetTimestamp();
        var trades = new List<(Guid Instrument, ExtensionDecision Decision)>();
        await foreach (var runTrade in runs.StreamTradesAsync(command.RunKey, false, cancellationToken))
        {
            trades.Add((runTrade.Trade.InstrumentId, new ExtensionDecision(runTrade.Trade.Key, DateTimeOffset.FromUnixTimeSeconds(runTrade.Trade.DecisionEpochSeconds))));
        }

        if (await records.FindAsync(command.RunKey, group.Version, cancellationToken) is not null)
        {
            ExtensionFeatureLog.Extracted(logger, command.RunKey[..12], group.Version, trades.Count, 0, 0, 0);
            return Result.Success(new ExtensionFeaturesOutcome(group.Version, trades.Count, 0, 0, true));
        }

        var ids = trades.Select(t => t.Instrument).Distinct().Order().ToList();
        var through = trades.Count == 0 ? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime) : trades.Max(t => IndiaTime.Today(t.Decision.At));
        var snapshot = await snapshots.ReadAsync(ids, through, cancellationToken);
        if (snapshot.IsFailure)
        {
            ExtensionFeatureLog.Refused(logger, snapshot.Error.Code);
            return Result.Failure<ExtensionFeaturesOutcome>(snapshot.Error);
        }

        var filings = (await filingTimes.ReadAsync(ids, cancellationToken)).GroupBy(f => f.InstrumentId).ToDictionary(g => g.Key, g => (IReadOnlyList<ResultsFilingTime>)g.ToList());
        IReadOnlyDictionary<DateOnly, double>? index = null;
        if (group.Version == EventFeatureCatalog.Version)
        {
            var days = await contextDays.ListAsync(cancellationToken);
            index = days.Count == 0 ? null : days.ToDictionary(d => d.Date, d => d.IndexClose);
        }

        var sources = new ExtensionSources(await calendars.GetAsync(cancellationToken), TimeSpan.FromMinutes(settings.LagMinutes), snapshot.Value, filings, index);
        var cells = new List<IReadOnlyList<FeatureCell>>(trades.Count);
        var rows = new List<ExtensionFeatureRow>(trades.Count);
        foreach (var instrument in trades.GroupBy(t => t.Instrument).OrderBy(g => g.Key))
        {
            var decisions = instrument.Select(t => t.Decision).ToList();
            var values = await extractor.ExtractAsync(group.Version, instrument.Key, decisions, sources, cancellationToken);
            foreach (var decision in decisions.OrderBy(d => d.At).ThenBy(d => d.TradeKey, StringComparer.Ordinal))
            {
                var row = values[decision.TradeKey];
                cells.Add(row);
                rows.Add(new ExtensionFeatureRow(command.RunKey, decision.TradeKey, group.Id, group.Version, decision.At, [.. row.Select(c => c.Value)]));
            }
        }

        var now = time.GetUtcNow();
        var watermark = filings.Values.SelectMany(f => f).Select(f => (DateTimeOffset?)f.BroadcastTs).Max();
        var unknown = cells.Sum(r => r.Count(c => !c.IsKnown));
        var record = new ExtensionExtractionRecord(
            command.RunKey,
            group.Id,
            group.Version,
            settings.LagMinutes,
            group.Version == FundamentalFeatureCatalog.Version ? ResultsParser.Version : null,
            snapshot.Value.Sha256,
            watermark,
            rows.Count,
            ExtensionNanCounts.Json(group.Names, cells),
            now);
        var (stored, _) = await rowsStore.AppendWithRecordAsync(rows, record, now, cancellationToken);
        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        ExtensionFeatureLog.Extracted(logger, command.RunKey[..12], group.Version, trades.Count, stored, unknown, elapsedMs);
        return Result.Success(new ExtensionFeaturesOutcome(group.Version, trades.Count, stored, unknown, false));
    }
}
