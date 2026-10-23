using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning;

/// <summary>
/// Proposes learned entry windows per cell (spec P6-14, ADR 0069): labels the closed trades of stored runs (P6-03), cuts the hold-out (P6-05), and learns a window per cell with the purged folds. Read-only: it stores nothing and applies nothing.
/// </summary>
internal sealed class ProposeEntryWindowsHandler(IRunStore runs, TimeProvider time, ILogger<ProposeEntryWindowsHandler> logger) : IQueryHandler<ProposeEntryWindowsQuery, Result<EntryWindowReport>>
{
    private static readonly string[] Styles = ["intraday", "swing"];

    public async ValueTask<Result<EntryWindowReport>> HandleAsync(ProposeEntryWindowsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var problems = Check(query);
        if (problems.Count > 0)
        {
            EntryWindowLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<EntryWindowReport>(LearningErrors.Invalid(problems));
        }

        var started = time.GetTimestamp();
        var read = await CellSourceReader.ReadAsync(runs, query.Sources, cancellationToken);
        if (read.IsFailure)
        {
            return Refuse(read.Error);
        }

        var set = TrainingSetBuilder.Build(query.Sources.Select(s => s.RunKey), read.Value.Trades.Select(t => t.Trade));
        if (set.IsFailure)
        {
            return Refuse(set.Error);
        }

        var cellOf = read.Value.Trades.ToDictionary(t => t.Trade.Key, t => t.CellKey, StringComparer.Ordinal);
        var rows = set.Value.Labels
            .Select(l => new ValidationRow(l.Key, l.Label, l.R, SessionClock.DateOf(l.DecisionEpochSeconds - HoldOut.BarSeconds), l.DecisionEpochSeconds, l.ResolvedEpochSeconds, cellOf[l.Key], ImmutableArray<double>.Empty))
            .ToList();
        var validation = query.Validation;
        string holdNote;
        if (query.HoldOutMonths > 0)
        {
            var split = HoldOut.Reserve(rows, query.HoldOutMonths);
            if (split.IsFailure)
            {
                return Refuse(split.Error);
            }

            rows = [.. split.Value.Development];
            validation = validation with { HoldOutStart = split.Value.Start };
            holdNote = $"The last {query.HoldOutMonths} months (from {split.Value.Start:yyyy-MM-dd}, {split.Value.Reserved.Count} trades, {split.Value.Purged} more purged) are held out and were not looked at.";
        }
        else
        {
            holdNote = "No hold-out was kept: every trade was used in the search.";
        }

        var learned = EntryWindowLearner.Learn(rows, query.Options, validation);
        if (learned.IsFailure)
        {
            return Refuse(learned.Error);
        }

        var report = learned.Value with { Limits = [.. learned.Value.Limits, holdNote] };
        var proposed = report.Cells.Count(c => c.Status == EntryWindowStatus.Proposed);
        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        EntryWindowLog.Completed(logger, report.Cells.Count, proposed, report.Rows, report.UsedFolds, elapsedMs);
        return Result.Success(report);
    }

    private Result<EntryWindowReport> Refuse(Error error)
    {
        EntryWindowLog.Refused(logger, error.Code);
        return Result.Failure<EntryWindowReport>(error);
    }

    private static List<FieldError> Check(ProposeEntryWindowsQuery query)
    {
        var problems = new List<FieldError>();
        if (query.Options is null || query.Validation is null)
        {
            problems.Add(new FieldError("options", "The options are missing."));
            return problems;
        }

        problems.AddRange(query.Options.Problems().Concat(query.Validation.Problems()).Select(p => new FieldError("options", p)));
        if (query.HoldOutMonths is < 0 or > 120)
        {
            problems.Add(new FieldError("holdOutMonths", "Hold-out months are 0 to 120."));
        }

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
