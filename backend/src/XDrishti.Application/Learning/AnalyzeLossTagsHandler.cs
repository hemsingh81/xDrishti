using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning;

/// <summary>Tags the losses of stored runs per cell (spec P6-15, ADR 0071). Read-only: it stores nothing, opens no lesson and applies nothing.</summary>
internal sealed class AnalyzeLossTagsHandler(IRunStore runs, TimeProvider time, ILogger<AnalyzeLossTagsHandler> logger) : IQueryHandler<AnalyzeLossTagsQuery, Result<LossTagReport>>
{
    private static readonly string[] Styles = ["intraday", "swing"];

    public async ValueTask<Result<LossTagReport>> HandleAsync(AnalyzeLossTagsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var problems = Check(query);
        if (problems.Count > 0)
        {
            LossTagLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<LossTagReport>(LearningErrors.Invalid(problems));
        }

        var started = time.GetTimestamp();
        var read = await CellSourceReader.ReadAsync(runs, query.Sources, cancellationToken);
        if (read.IsFailure)
        {
            return Refuse(read.Error);
        }

        var inputs = read.Value.Trades.Select(t => new LossTagInput(t.CellKey, t.Trade, ResultOf(t.Trade))).ToList();
        var report = LossTagAnalyzer.Analyze(inputs, query.Options);
        if (report.IsFailure)
        {
            return Refuse(report.Error);
        }

        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        var growing = report.Value.Cells.Sum(c => c.Growing.Count);
        LossTagLog.Completed(logger, report.Value.Cells.Count, report.Value.Trades, report.Value.Losses, growing, elapsedMs);
        return report;
    }

    /// <summary>The result in the unit of the cell: net when the trade was charged, else from the filled prices (as the cell statistics read it).</summary>
    private static double ResultOf(SimTrade t) => t.Costs == CostBasis.Charged && t.Costing is not null ? t.Costing.RNet : t.RFilled;

    private Result<LossTagReport> Refuse(Error error)
    {
        LossTagLog.Refused(logger, error.Code);
        return Result.Failure<LossTagReport>(error);
    }

    private static List<FieldError> Check(AnalyzeLossTagsQuery query)
    {
        var problems = new List<FieldError>();
        if (query.Options is null)
        {
            problems.Add(new FieldError("options", "The options are missing."));
            return problems;
        }

        problems.AddRange(query.Options.Problems().Select(p => new FieldError("options", p)));
        if (query.Sources is null || query.Sources.Count is < 1 or > ComputeCellStatsHandler.MaxSources)
        {
            problems.Add(new FieldError("sources", $"Name 1 to {ComputeCellStatsHandler.MaxSources} runs."));
            return problems;
        }

        foreach (var s in query.Sources)
        {
            if (!RunRecordText.IsHex64(s.RunKey))
            {
                problems.Add(new FieldError("sources.runKey", "A run key is 64 lower-case hex characters."));
            }

            if (!Styles.Contains(s.Style, StringComparer.Ordinal))
            {
                problems.Add(new FieldError("sources.style", "The style is intraday or swing."));
            }

            if (new CellId("x", "x", "x", "x", s.Exit ?? string.Empty).Problems().Count > 0)
            {
                problems.Add(new FieldError("sources.exit", "The exit label is required, at most 64 characters and without '|'."));
            }
        }

        if (query.Sources.Select(s => s.RunKey).Distinct(StringComparer.Ordinal).Count() != query.Sources.Count)
        {
            problems.Add(new FieldError("sources", "A run is named twice."));
        }

        return problems;
    }
}
