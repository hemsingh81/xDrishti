using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning;

/// <summary>
/// Judges a knob change and records it (spec P6-11, ADR 0072). It reads the two stored runs, checks that they were made on the same data (engine, window, universe, CSV acknowledgement, schema versions, strategy; the strategy definition too unless the knob is a
/// parameter or exit knob, which changes it) and the same cost assumptions (a slippage knob may differ only in the <c>slippage.*</c> fill fields, and its direction is read from the stored fills, not the typed values), cuts the hold-out
/// (P6-05), counts the experiments of the last 30 days in the ledger as the trials, judges with <see cref="ExperimentJudge"/> and appends the experiment, whatever the outcome: every look is a trial. The same experiment again is not judged twice.
/// </summary>
internal sealed class RunExperimentHandler(IRunStore runs, IExperimentLedger ledger, TimeProvider time, ILogger<RunExperimentHandler> logger) : ICommandHandler<RunExperimentCommand, RunExperimentOutcome>
{
    private const int MaxAttempts = 5;

    private sealed record Seen(DateOnly TradeDate, double R, long DecisionEpochSeconds, long ExitEpochSeconds, bool Costed);

    public async ValueTask<Result<RunExperimentOutcome>> HandleAsync(RunExperimentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = Check(command);
        if (problems.Count > 0)
        {
            return Refuse(LearningErrors.Invalid(problems));
        }

        var started = time.GetTimestamp();
        var key = ExperimentCanonical.Key(command);
        if (await ledger.FindByKeyAsync(key, cancellationToken) is { } existing)
        {
            ExperimentLog.AlreadyRecorded(logger, existing.Id);
            return Result.Success(Outcome(existing, true));
        }

        var baselineHeader = await runs.FindAsync(command.BaselineRunKey, cancellationToken);
        var candidateHeader = await runs.FindAsync(command.CandidateRunKey, cancellationToken);
        if (baselineHeader is null || candidateHeader is null)
        {
            return Refuse(LearningErrors.RunNotFound(baselineHeader is null ? command.BaselineRunKey : command.CandidateRunKey));
        }

        var differs = Differs(baselineHeader, candidateHeader, command.Knob.Kind);
        if (differs is not null)
        {
            return Refuse(ExperimentErrors.AssumptionsDiffer(differs));
        }

        var fillCost = FillCostChange.NotApplicable;
        if (command.Knob.Kind == ExperimentKnobKind.Slippage)
        {
            var compared = FillCostComparison.Compare(baselineHeader.Fills, candidateHeader.Fills);
            if (compared.IsFailure)
            {
                return Refuse(compared.Error);
            }

            fillCost = compared.Value;
        }

        var baseline = await ReadAsync(command.BaselineRunKey, cancellationToken);
        var candidate = await ReadAsync(command.CandidateRunKey, cancellationToken);
        if (baseline.IsFailure)
        {
            return Refuse(baseline.Error);
        }

        if (candidate.IsFailure)
        {
            return Refuse(candidate.Error);
        }

        var added = candidate.Value.Keys.Count(k => !baseline.Value.ContainsKey(k));
        if (added > 0)
        {
            return Refuse(ExperimentErrors.EntriesAdded(added));
        }

        if (baseline.Value.Count == 0 || baseline.Value.Values.Select(s => s.Costed).Concat(candidate.Value.Values.Select(s => s.Costed)).Distinct().Count() > 1)
        {
            return Refuse(baseline.Value.Count == 0 ? Error.Validation("experiment.no_trades", "The baseline run has no closed trades.") : ExperimentErrors.BasisDiffers());
        }

        var options = command.Options;
        string holdNote;
        HashSet<string>? development = null;
        if (command.HoldOutMonths > 0)
        {
            var rows = baseline.Value
                .Select(b => new ValidationRow(b.Key, b.Value.R > 0 ? 1 : 0, b.Value.R, b.Value.TradeDate, b.Value.DecisionEpochSeconds, Math.Max(b.Value.ExitEpochSeconds, candidate.Value.TryGetValue(b.Key, out var c) ? c.ExitEpochSeconds : 0), "x", ImmutableArray<double>.Empty))
                .ToList();
            var split = HoldOut.Reserve(rows, command.HoldOutMonths);
            if (split.IsFailure)
            {
                return Refuse(split.Error);
            }

            development = [.. split.Value.Development.Select(r => r.Key)];
            options = options with { HoldOutStart = split.Value.Start };
            holdNote = string.Create(CultureInfo.InvariantCulture, $"The last {command.HoldOutMonths} months (from {split.Value.Start:yyyy-MM-dd}, {split.Value.Reserved.Count} trades, {split.Value.Purged} more purged) are held out and were not looked at.");
        }
        else
        {
            holdNote = "No hold-out was kept: every trade was used, so this cannot be called better.";
            options = options with { NoHoldOut = true };
        }

        var now = time.GetUtcNow();
        var since = now.AddDays(-ExperimentOptions.TrialWindowDays);
        var others = await ledger.CountSinceAsync(since, key, cancellationToken);
        var trials = Math.Max(command.Options.Trials, others + 1);
        var trades = baseline.Value
            .Where(b => development is null || development.Contains(b.Key))
            .Select(b => new ExperimentTrade(b.Key, b.Value.TradeDate, b.Value.R, candidate.Value.TryGetValue(b.Key, out var c) ? c.R : null))
            .ToList();
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var judged = ExperimentJudge.Judge(command.Knob, trades, options with { Trials = trials }, fillCost);
            if (judged.IsFailure)
            {
                return Refuse(judged.Error);
            }

            var report = judged.Value;
            var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
            var text = report.ToText()
                + "limit: " + holdNote + "\n"
                + string.Create(CultureInfo.InvariantCulture, $"timing: reading both runs and judging them took {elapsedMs} ms (measured). The replay that made the candidate run is a separate step and was not measured here: the 10-minute target for a replay and comparison is not claimed (not measured).\n");
            var stored = new ExperimentToStore(
                key,
                command.Knob,
                command.BaselineRunKey,
                command.CandidateRunKey,
                report.From,
                report.To,
                options.HoldOutStart,
                report.Outcome,
                report.Options.Trials,
                report.UsedFolds,
                report.FoldsBetter,
                report.FoldsNeeded,
                report.RequiredImprovementR,
                report.BaselineTrades,
                report.CandidateTrades,
                report.PooledBaselineMeanR,
                report.PooledCandidateMeanR,
                report.BaselineTotalR,
                report.CandidateTotalR,
                report.Options.ExpectedLiveGapR,
                elapsedMs,
                string.Join('\n', report.Reasons),
                text,
                ExperimentCanonical.Parameters(command.Options, command.HoldOutMonths),
                now,
                command.Actor);
            RecordedExperiment recorded;
            try
            {
                recorded = await ledger.AppendAsync(stored, command.Options.Trials, since, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                ExperimentLog.Failed(logger);
                throw;
            }

            if (recorded.RequiredTrials > 0)
            {
                // Another experiment was recorded since the count: judge again with the ledger's number.
                trials = recorded.RequiredTrials;
                continue;
            }

            if (!recorded.Created)
            {
                // A concurrent identical request won: answer with what is stored, not with this judgement.
                var winner = await ledger.FindAsync(recorded.Id, cancellationToken);
                if (winner is null)
                {
                    return Refuse(ExperimentErrors.Busy());
                }

                ExperimentLog.AlreadyRecorded(logger, winner.Id);
                return Result.Success(Outcome(winner, true));
            }

            var outcomeName = report.Outcome.ToString();
            ExperimentLog.Recorded(logger, recorded.Id, outcomeName, report.BaselineTrades, report.UsedFolds, report.Options.Trials, elapsedMs);
            return Result.Success(new RunExperimentOutcome(recorded.Id, false, outcomeName, report.Options.Trials, report.Reasons, text, elapsedMs));
        }

        return Refuse(ExperimentErrors.Busy());
    }

    private static RunExperimentOutcome Outcome(StoredExperiment s, bool already)
    {
        var e = s.Experiment;
        return new RunExperimentOutcome(s.Id, already, e.Outcome.ToString(), e.Trials, e.Reasons.Length == 0 ? [] : e.Reasons.Split('\n'), e.ReportText, e.CompareMs);
    }

    // The two runs must be on the same data and the same costs. A parameter or exit knob changes the strategy definition (its hash may differ); nothing else may, and the strategy id never does. A slippage knob's fill rules are compared apart.
    private static string? Differs(StoredRunHeader a, StoredRunHeader b, ExperimentKnobKind kind)
    {
        if (!string.Equals(a.CostsSheetId, b.CostsSheetId, StringComparison.Ordinal) || !string.Equals(a.CostsRatesSha256, b.CostsRatesSha256, StringComparison.Ordinal))
        {
            return "the cost sheet";
        }

        if (!string.Equals(a.EngineVersion, b.EngineVersion, StringComparison.Ordinal) || a.RecordSchemaVersion != b.RecordSchemaVersion || a.TradeSchemaVersion != b.TradeSchemaVersion)
        {
            return "the engine or the record schema (the runs were not made by the same engine)";
        }

        if (a.From != b.From || a.To != b.To || !string.Equals(a.UniverseKind, b.UniverseKind, StringComparison.Ordinal) || !string.Equals(a.UniverseLabel, b.UniverseLabel, StringComparison.Ordinal) || a.AcknowledgeCsvAdjusted != b.AcknowledgeCsvAdjusted)
        {
            return "the data (window, universe or CSV acknowledgement): the runs were not made on the same data";
        }

        if (!string.Equals(a.StrategyId, b.StrategyId, StringComparison.Ordinal))
        {
            return "the strategy";
        }

        if (kind == ExperimentKnobKind.Slippage && !string.Equals(a.DefinitionSha256, b.DefinitionSha256, StringComparison.Ordinal))
        {
            return "the strategy definition (only a parameter or exit knob changes it)";
        }

        if (kind != ExperimentKnobKind.Slippage && !string.Equals(a.Fills, b.Fills, StringComparison.Ordinal))
        {
            return "the fill basis";
        }

        return null;
    }

    private async ValueTask<Result<Dictionary<string, Seen>>> ReadAsync(string runKey, CancellationToken cancellationToken)
    {
        var seen = new Dictionary<string, Seen>(StringComparer.Ordinal);
        await foreach (var runTrade in runs.StreamTradesAsync(runKey, false, cancellationToken))
        {
            var t = runTrade.Trade;
            var costed = t.Costs == CostBasis.Charged && t.Costing is not null;
            var trade = new Seen(SessionClock.DateOf(t.DecisionEpochSeconds - HoldOut.BarSeconds), costed ? t.Costing!.RNet : t.RFilled, t.DecisionEpochSeconds, t.ExitEpochSeconds, costed);
            if (!seen.TryAdd(t.Key, trade))
            {
                return Result.Failure<Dictionary<string, Seen>>(ExperimentErrors.DuplicateTrade(runKey));
            }
        }

        return Result.Success(seen);
    }

    private Result<RunExperimentOutcome> Refuse(Error error)
    {
        ExperimentLog.Refused(logger, error.Code);
        return Result.Failure<RunExperimentOutcome>(error);
    }

    private static List<FieldError> Check(RunExperimentCommand command)
    {
        var problems = new List<FieldError>();
        if (command.Knob is null || command.Options is null)
        {
            problems.Add(new FieldError("knob", "The knob and the options are required."));
            return problems;
        }

        problems.AddRange(command.Knob.Problems().Select(p => new FieldError("knob", p)));
        problems.AddRange(command.Options.Problems().Select(p => new FieldError("options", p)));
        if (!RunRecordText.IsHex64(command.BaselineRunKey) || !RunRecordText.IsHex64(command.CandidateRunKey))
        {
            problems.Add(new FieldError("runKey", "A run key is 64 lower-case hex characters."));
        }
        else if (string.Equals(command.BaselineRunKey, command.CandidateRunKey, StringComparison.Ordinal))
        {
            problems.Add(new FieldError("runKey", "The baseline and the candidate are the same run."));
        }

        if (command.HoldOutMonths is < 0 or > 120)
        {
            problems.Add(new FieldError("holdOutMonths", "Hold-out months are 0 to 120."));
        }

        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        return problems;
    }
}
