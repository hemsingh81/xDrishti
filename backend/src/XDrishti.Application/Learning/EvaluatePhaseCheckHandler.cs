using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The phase check (spec P6-18, ADR 0080). It reads the champion (else the newest challenger) from the registry and takes its stored verdict and calibration as they are; it re-reads the two runs of the newest experiment and judges them again, timing that with the
/// <see cref="TimeProvider"/>; and it hands the facts to the pure <see cref="PhaseCheck"/>. Nothing is written and no statistic is computed here that a stored record already holds. The judging is the experiment judge with default rules and the stored knob, trials and hold-out,
/// run only to time it and to see that it gives the stored outcome again.
/// </summary>
internal sealed class EvaluatePhaseCheckHandler(IModelRegistry models, IExperimentLedger ledger, IRunStore runs, TimeProvider time, ILogger<EvaluatePhaseCheckHandler> logger)
    : IQueryHandler<EvaluatePhaseCheckQuery, Result<PhaseCheckView>>
{
    public async ValueTask<Result<PhaseCheckView>> HandleAsync(EvaluatePhaseCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.ReplaySeconds is { } s && (!double.IsFinite(s) || s <= 0 || s > 1_000_000))
        {
            PhaseCheckLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<PhaseCheckView>(LearningErrors.Invalid([new FieldError("replaySeconds", "The replay seconds are a number above 0 and at most 1000000.")]));
        }

        var model = await FindModelAsync(cancellationToken);
        var (replay, problem) = await MeasureAsync(query.ReplaySeconds, cancellationToken);
        var report = PhaseCheck.Evaluate(model, replay, new ValidationOptions().MaxCalibrationErrorPp, problem);
        var met = report.Criteria.Count(c => c.Verdict == PhaseVerdict.Met);
        var notMet = report.Criteria.Count(c => c.Verdict == PhaseVerdict.NotMet);
        var notEvaluable = report.Criteria.Count(c => c.Verdict == PhaseVerdict.NotEvaluable);
        PhaseCheckLog.Evaluated(logger, met, notMet, notEvaluable);
        return Result.Success(new PhaseCheckView(report, report.ToText()));
    }

    private async ValueTask<PhaseModelFacts?> FindModelAsync(CancellationToken cancellationToken)
    {
        var champions = await models.ListAsync(ModelRole.Champion, 1, cancellationToken);
        var champion = champions.Count == 0 ? null : champions[0];
        var challengers = champion is null ? await models.ListAsync(ModelRole.Challenger, 1, cancellationToken) : [];
        var stored = champion ?? (challengers.Count == 0 ? null : challengers[0]);
        if (stored is null)
        {
            return null;
        }

        var m = stored.Model;
        return new PhaseModelFacts(
            stored.Id,
            champion is null ? "newest challenger" : "champion",
            m.Verdict,
            m.UsedFolds,
            m.FoldsBeatingBoth,
            m.FoldsNeeded,
            m.PooledBeatsBoth,
            m.PooledEcePp,
            m.HoldOutStart,
            m.FromDate,
            m.ToDate,
            Split(m.Reasons),
            ChampionRule.Reasons(m.Verdict, m.HoldOutStart, m.UsedFolds, m.Trials, stored.RequiredTrials));
    }

    private async ValueTask<(PhaseReplayFacts? Replay, string? Problem)> MeasureAsync(double? suppliedReplaySeconds, CancellationToken cancellationToken)
    {
        var listed = await ledger.ListAsync(1, cancellationToken);
        if (listed.Count == 0)
        {
            return (null, null);
        }

        var newest = listed[0];

        var e = newest.Experiment;
        var started = time.GetTimestamp();
        var baselineHeader = await runs.FindAsync(e.BaselineRunKey, cancellationToken);
        var candidateHeader = await runs.FindAsync(e.CandidateRunKey, cancellationToken);
        if (baselineHeader is null || candidateHeader is null)
        {
            return Unavailable(newest.Id, LearningErrors.RunNotFoundCode, "a stored run of the experiment is not found");
        }

        var fillCost = FillCostChange.NotApplicable;
        if (e.Knob.Kind == ExperimentKnobKind.Slippage)
        {
            var compared = FillCostComparison.Compare(baselineHeader.Fills, candidateHeader.Fills);
            if (compared.IsFailure)
            {
                return Unavailable(newest.Id, compared.Error.Code, compared.Error.Message);
            }

            fillCost = compared.Value;
        }

        var baseline = await ExperimentRunReader.ReadAsync(runs, e.BaselineRunKey, cancellationToken);
        var candidate = await ExperimentRunReader.ReadAsync(runs, e.CandidateRunKey, cancellationToken);
        if (baseline.IsFailure || candidate.IsFailure)
        {
            var error = baseline.IsFailure ? baseline.Error : candidate.Error;
            return Unavailable(newest.Id, error.Code, error.Message);
        }

        var trades = baseline.Value
            .Where(b => e.HoldOutStart is not { } hold || b.Value.TradeDate < hold)
            .Select(b => new ExperimentTrade(b.Key, b.Value.TradeDate, b.Value.R, candidate.Value.TryGetValue(b.Key, out var c) ? c.R : null))
            .ToList();
        var options = new ExperimentOptions { Trials = e.Trials, HoldOutStart = e.HoldOutStart, NoHoldOut = e.HoldOutStart is null };
        var judged = ExperimentJudge.Judge(e.Knob, trades, options, fillCost);
        if (judged.IsFailure)
        {
            return Unavailable(newest.Id, judged.Error.Code, judged.Error.Message);
        }

        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        PhaseCheckLog.CompareMeasured(logger, newest.Id, trades.Count, elapsedMs);
        return (new PhaseReplayFacts(newest.Id, trades.Count, elapsedMs, e.CompareMs, e.Outcome.ToString(), judged.Value.Outcome.ToString(), suppliedReplaySeconds), null);
    }

    private (PhaseReplayFacts? Replay, string? Problem) Unavailable(long experimentId, string code, string message)
    {
        PhaseCheckLog.CompareUnavailable(logger, experimentId, code);
        return (null, message);
    }

    private static string[] Split(string reasons) => reasons.Length == 0 ? [] : reasons.Split('\n');
}
