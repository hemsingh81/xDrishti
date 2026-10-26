using System.Collections.Immutable;
using System.Globalization;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>
/// What starting a replay on a stored run would mean (spec P8-18 criterion 12). Read only: it writes nothing and starts nothing. It says, before the owner clicks, the next trial number, the improvement that trial must show, the folds the baseline's own trades have, the best the
/// outcome can be on them, the measured replay time (none until a job measured one) and how many starts are left today.
/// </summary>
internal sealed class GetExperimentPreviewHandler(IRunStore runs, ReplayPlanner planner, IExperimentLedger ledger, IExperimentJobStore jobs, ExperimentJobSettings settings, TimeProvider clock)
    : IQueryHandler<GetExperimentPreviewQuery, Result<ExperimentPreviewView>>
{
    private const string NoKey = "0000000000000000000000000000000000000000000000000000000000000000";

    public async ValueTask<Result<ExperimentPreviewView>> HandleAsync(GetExperimentPreviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!RunRecordText.IsHex64(query.BaselineRunKey))
        {
            return Result.Failure<ExperimentPreviewView>(LearningErrors.Invalid([new FieldError("baselineRunKey", "A run key is 64 lower-case hex characters.")]));
        }

        var header = await runs.FindAsync(query.BaselineRunKey, cancellationToken);
        var record = header is null ? null : await runs.ReadRecordTextAsync(query.BaselineRunKey, cancellationToken);
        if (header is null || record is null)
        {
            return Result.Failure<ExperimentPreviewView>(LearningErrors.RunNotFound(query.BaselineRunKey));
        }

        var plan = await planner.PlanAsync(header, record, cancellationToken);
        var now = clock.GetUtcNow();
        var others = await ledger.CountSinceAsync(now.AddDays(-ExperimentOptions.TrialWindowDays), NoKey, cancellationToken);
        var next = others + 1;
        var required = new ExperimentOptions { Trials = next }.RequiredImprovementR;
        var folds = await FoldsAsync(query.BaselineRunKey, cancellationToken);
        var minFolds = ValidationOptions.DefaultMinFolds;
        var capped = folds < minFolds;
        var c = CultureInfo.InvariantCulture;
        var measured = await jobs.LatestMeasuredAsync(cancellationToken);
        var estimate = measured?.ReplayMs is { } ms
            ? new PreviewEstimateView("value", ms, string.Create(c, $"The last replay took {ms / 1000} s for {measured.Instruments} instruments, measured on this machine; it is not a promise."))
            : new PreviewEstimateView("not_evaluable", null, "No replay time measured yet.");
        var jobsToday = await jobs.CountSinceAsync(now - ExperimentJobSettings.Window, cancellationToken);
        var active = await jobs.ActiveAsync(cancellationToken);
        var baseline = plan.IsSuccess ? plan.Value.Baseline : null;
        var exit = baseline?.Exit is { } e ? e.Id : null;
        var limits = new List<string>
        {
            "A replay counts as a look: every judged run raises the improvement the next one must show, for 30 days.",
            "The knob is chosen by looking at the same history it is judged on (ISS-19).",
            ExperimentJobViews.Nothing,
        };
        return Result.Success(new ExperimentPreviewView(
            new PreviewBaselineView(header.StrategyId, header.From, header.To, header.Instruments, header.CostsSheetId is not null, exit),
            plan.IsSuccess,
            plan.IsSuccess ? null : plan.Error.Message,
            plan.IsSuccess ? [.. plan.Value.Offers.Select(o => new PreviewKnobView(o.Kind == ExperimentKnobKind.Exit ? "exit" : "slippage", o.Name, o.Current, o.Min, o.Max, o.Note))] : [],
            next,
            required,
            folds,
            minFolds,
            capped ? "Inconclusive" : "Better",
            capped ? string.Create(c, $"On this data ({folds} usable folds, {minFolds} needed) a replay can at best be Inconclusive. It still counts as a look.") : null,
            estimate,
            jobsToday,
            settings.MaxJobsPerDay,
            active?.Id,
            limits));
    }

    // The folds the baseline's own trades fill (at least 10 in a test window, after the 6-month hold-out): an upper bound, the candidate's side is not known yet.
    private async Task<int> FoldsAsync(string runKey, CancellationToken cancellationToken)
    {
        var seen = await ExperimentRunReader.ReadAsync(runs, runKey, cancellationToken);
        if (seen.IsFailure || seen.Value.Count == 0)
        {
            return 0;
        }

        var rows = seen.Value.Select(b => new ValidationRow(b.Key, b.Value.R > 0 ? 1 : 0, b.Value.R, b.Value.TradeDate, b.Value.DecisionEpochSeconds, b.Value.ExitEpochSeconds, "x", ImmutableArray<double>.Empty)).ToList();
        var split = HoldOut.Reserve(rows, 6);
        if (split.IsFailure)
        {
            return 0;
        }

        var kept = split.Value.Development.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var dates = seen.Value.Where(t => kept.Contains(t.Key)).Select(t => t.Value.TradeDate).Order().ToList();
        if (dates.Count == 0)
        {
            return 0;
        }

        var options = new ExperimentOptions();
        var windows = ExitSelector.Windows(dates[0], dates[^1], new SelectionOptions(options.MinTrainMonths, options.TestMonths)).Windows;
        return windows.Count(w => dates.Count(d => d >= w.Start && d <= w.End) >= options.MinFoldTrades);
    }
}
