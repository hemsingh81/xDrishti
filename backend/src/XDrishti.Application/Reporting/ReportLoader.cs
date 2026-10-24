using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// Reads the runs and closed trades a report needs through the port and builds the population (P8-06): the run set (named or the default), the filters of the query, the basis, and the population rule of the engine, whose refusals pass through unchanged.
/// A net basis is allowed only by the owner's stored confirmation of the charge sheet (<see cref="IChargeSheetConfirmationSource"/>), read here and nowhere else; nothing in the query can supply it.
/// </summary>
internal sealed class ReportLoader(IReportTradeSource source, IChargeSheetConfirmationSource confirmations, TimeProvider time, ILogger<ReportLoader> logger)
{
    public async ValueTask<Result<LoadedReport>> LoadAsync(ReportQuery query, string kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var started = time.GetTimestamp();
        var result = await BuildAsync(query, started, kind, cancellationToken);
        if (result.IsFailure)
        {
            ReportLog.Refused(logger, kind, result.Error.Code);
        }

        return result;
    }

    private async ValueTask<Result<LoadedReport>> BuildAsync(ReportQuery query, long started, string kind, CancellationToken cancellationToken)
    {
        var all = await source.ListRunsAsync(cancellationToken);
        List<ReportRunInfo> chosen;
        var isDefault = query.Runs.Count == 0;
        if (isDefault)
        {
            chosen = [.. RunSelection.Default(all).Chosen];
        }
        else
        {
            chosen = [];
            foreach (var key in query.Runs)
            {
                var run = all.FirstOrDefault(r => string.Equals(r.RunKey, key, StringComparison.Ordinal));
                if (run is null)
                {
                    return Result.Failure<LoadedReport>(ReportErrors.UnknownRun(key));
                }

                chosen.Add(run);
            }
        }

        var used = chosen.Where(query.Filters.KeepsRun).ToList();
        var options = new ReportOptions { Basis = query.Basis };
        if (query.Basis == ReportBasis.Net)
        {
            // The only source of a confirmation: the stored one. A query cannot carry it.
            var confirmation = await confirmations.FindAsync(cancellationToken);
            if (confirmation is null)
            {
                return Result.Failure<LoadedReport>(ReportErrors.NetNotConfirmed());
            }

            var mismatch = used.FirstOrDefault(r => !string.Equals(r.CostsSheetId, confirmation.SheetId, StringComparison.Ordinal));
            if (mismatch is not null)
            {
                return Result.Failure<LoadedReport>(ReportErrors.NetSheetMismatch(mismatch.RunKey, mismatch.CostsSheetId, confirmation.SheetId));
            }

            options = options with { NetBasisConfirmation = confirmation };
        }

        var trades = new List<ReportTrade>();
        if (used.Count > 0)
        {
            await foreach (var trade in source.StreamTradesAsync([.. used.Select(r => r.RunKey)], query.Filters.ToTradeFilter(), cancellationToken))
            {
                trades.Add(trade);
            }
        }

        var population = ReportPopulation.Create([.. used.Select(r => r.ToReportRun())], trades, options);
        if (population.IsFailure)
        {
            return Result.Failure<LoadedReport>(population.Error);
        }

        var basis = query.Basis == ReportBasis.Net ? "net" : "gross";
        var milliseconds = (long)time.GetElapsedTime(started).TotalMilliseconds;
        ReportLog.Loaded(logger, kind, used.Count, trades.Count, basis, milliseconds);
        return Result.Success(new LoadedReport(population.Value, used, isDefault));
    }
}
