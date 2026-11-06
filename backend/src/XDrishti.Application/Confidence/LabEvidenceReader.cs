using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Readiness;

namespace XDrishti.Application.Confidence;

/// <summary>What the Lab produced for one strategy version, in the inputs of the scorecard (rows 1 to 12, the folds and two hard-fail terms).</summary>
/// <param name="Checklist">Rows 1 to 12 filled where the Lab has the input; the rest default (unavailable).</param>
/// <param name="Folds">The folds of the verdict (P6-05), or null.</param>
/// <param name="PositiveFolds">The positive folds, or null.</param>
/// <param name="CostSheet">The cost-sheet hard-fail term.</param>
/// <param name="HoldOutReused">The hold-out hard-fail term.</param>
/// <param name="Notes">The honesty sentences about what was read.</param>
internal sealed record LabEvidence(ChecklistInputs Checklist, int? Folds, int? PositiveFolds, FaultState CostSheet, FaultState HoldOutReused, IReadOnlyList<string> Notes);

/// <summary>
/// Reads the stored Lab results of one strategy version (the newest finished full run's verdict, the registration and the hold-out) through the existing queries and maps them to the inputs of the checklist (spec CF-11 D1). A producer that judges its own thresholds
/// (CF-05 cost stress, CF-07 nulls, CF-06 hold-out) hands over its own word; nothing is re-judged here and nothing is invented: a block the verdict does not have is an input that does not exist. Read only.
/// </summary>
internal sealed class LabEvidenceReader(
    IQueryHandler<ListLabRunsQuery, Result<IReadOnlyList<LabRunView>>> runs,
    IQueryHandler<GetLabRunQuery, Result<LabRunView>> run,
    IQueryHandler<GetRegistrationQuery, Result<RegistrationView>> registration,
    IQueryHandler<GetHoldOutQuery, Result<HoldOutView>> holdOut,
    ICostConfirmationFact confirmation)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async ValueTask<LabEvidence> ReadAsync(string strategy, int? version, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var rowNotes = new Dictionary<int, string>();
        if (version is not { } v)
        {
            rowNotes[1] = "the strategy has no version in the Lab.";
            notes.Add("The strategy has no version in the Lab, so no Lab result is read.");
            return new LabEvidence(new ChecklistInputs { Notes = rowNotes }, null, null, FaultState.Unavailable, FaultState.Unavailable, notes);
        }

        var reg = await registration.HandleAsync(new GetRegistrationQuery(strategy, v), cancellationToken);
        bool? registered = reg.IsSuccess ? !string.IsNullOrWhiteSpace(reg.Value.TextHash) : reg.Error.Kind == ErrorKind.NotFound ? false : null;
        var hold = await holdOut.HandleAsync(new GetHoldOutQuery(), cancellationToken);
        var results = hold.IsSuccess ? hold.Value.Unlocks.Where(u => u.StrategyId == strategy && u.Version == v).OrderByDescending(u => u.UnlockedAt).ToList() : null;
        var holdStatus = results is null ? RowStatus.Unavailable : results.Count == 0 ? RowStatus.Unavailable : results[0].Passed && !results[0].ReusedWindow ? RowStatus.Pass : RowStatus.Fail;
        rowNotes[12] = results is null ? "the hold-out record could not be read." : results.Count == 0 ? "the hold-out has not been unlocked yet." : results[0].Reason;
        var reused = results is null ? FaultState.Unavailable : results.Any(r => r.ReusedWindow && r.Passed) ? FaultState.Fault : FaultState.Clear;

        var (verdict, why) = await VerdictAsync(strategy, v, cancellationToken);
        var checklist = new ChecklistInputs { Registered = registered, HoldOut = holdStatus };
        if (verdict is null)
        {
            notes.Add(why);
            foreach (var row in new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 })
            {
                rowNotes[row] = why;
            }

            rowNotes[1] = registered is null ? "the registration could not be read." : registered == false ? "no registration is stored for this version." : string.Empty;
            return new LabEvidence(checklist with { Notes = rowNotes }, null, null, FaultState.Unavailable, reused, notes);
        }

        var evidence = verdict.Evidence;
        var cells = verdict.Cells.Where(c => c.NoChoiceReason is null).Select(c => c.OosTrades).ToList();
        double? expectancy = evidence.ExpectancyR;
        var sr = verdict.DeflatedSharpe?.Sr;
        double? sd = expectancy is { } m && sr is { } s && Math.Abs(s) > 1e-9 ? Math.Abs(m / s) : null;
        var stress = verdict.CostStress?.Rows.Where(r => r.Multiplier > 1).OrderBy(r => r.Multiplier).ToList();
        var nulls = verdict.Nulls;
        var years = verdict.Robustness?.ByYear;
        double? yearShare = null;
        if (years is { Count: > 0 })
        {
            var total = years.Sum(y => y.TotalR);
            yearShare = total > 0 ? years.Max(y => y.TotalR) / total : 1.0;
        }

        var mc = verdict.Robustness?.MonteCarlo;
        double? p95 = mc is null ? null : Math.Max(mc.DrawdownP95, verdict.Robustness?.MonteCarloBlock?.DdP95 ?? double.MinValue);
        checklist = checklist with
        {
            NEff = evidence.Dependence?.NEff,
            MinTradesPerCell = cells.Count == 0 ? null : cells.Min(),
            ExpectancyR = expectancy,
            Factor = Math.Max(verdict.AdjustmentFactor, verdict.Trials?.HonestFactor ?? 0),
            SdR = sd,
            CostStress15 = stress is { Count: > 0 } ? Word(stress[0].Status) : RowStatus.Unavailable,
            CostStress2 = stress is { Count: > 0 } ? Word(stress[^1].Status) : RowStatus.Unavailable,
            FillDelay = Word(verdict.CostStress?.Delay?.Status),
            RandomEntry = Word(nulls?.RandomEntry.Status),
            Placebo = Word(nulls?.Placebos?.Status),
            Mirror = Word(nulls?.Mirror?.Status),
            Plateau = Word(nulls?.Plateau?.Status),
            MaxYearShare = yearShare,
            ObservedMaxDrawdownR = mc?.ObservedMaxDrawdownR,
            McDrawdownP95 = p95,
        };
        if (evidence.Dependence?.NEff is null)
        {
            rowNotes[2] = "the effective n is not computed (Confidence:EffectiveN:Mode is Off, or there are too few trades).";
            rowNotes[6] = rowNotes[2];
        }

        if (sd is null)
        {
            rowNotes[6] = "the spread of the R is derived from the deflated Sharpe block, which is not in this verdict.";
        }

        if (verdict.CostStress is null)
        {
            rowNotes[4] = rowNotes[5] = "the cost stress is not computed (Confidence:CostStress:Mode is Off).";
        }

        if (nulls is null)
        {
            rowNotes[7] = rowNotes[8] = rowNotes[9] = "the nulls are not computed (Confidence:Nulls:Mode is Off, or the run was not a robust one).";
        }

        if (years is null or { Count: 0 })
        {
            rowNotes[10] = "the verdict has no by-year table (run the robust method).";
        }

        if (mc is null)
        {
            rowNotes[11] = "the verdict has no Monte Carlo block (run the robust method).";
        }

        notes.Add(string.Create(Inv, $"Lab results are read from the newest finished full run of version {v}. Rows 2, 3 and 6 are on the development out-of-sample trades (pre-hold-out); they are not recomputed with the hold-out trades, because the hold-out's effective n and spread are not stored."));
        notes.Add("Row 3 uses the verdict's own out-of-sample expectancy (the figure of the P5 expectancy gate), and the larger of the factor the gates used and the honest factor of the full trial count; the standard deviation behind row 6 is that expectancy over the per-trade Sharpe of the deflated-Sharpe block.");
        if (verdict.CostStress is { CostsChecked: false })
        {
            notes.Add("The Lab run's costs were not checked against a bill, so its net figures are not vouched for.");
        }

        var costSheet = await CostSheetAsync(verdict, cancellationToken);
        return new LabEvidence(checklist with { Notes = rowNotes }, evidence.Folds, evidence.PositiveFolds, costSheet, reused, notes);
    }

    private async ValueTask<(LabVerdictView? Verdict, string Why)> VerdictAsync(string strategy, int version, CancellationToken cancellationToken)
    {
        var listed = await runs.HandleAsync(new ListLabRunsQuery(strategy, "succeeded", 200), cancellationToken);
        var none = string.Create(Inv, $"No finished full Lab run of version {version} was found (run the Lab full test).");
        if (listed.IsFailure)
        {
            return (null, "The Lab runs could not be read.");
        }

        var pick = listed.Value.Where(r => r.Strategy.Version == version && LabSettings.RunsFullTest(r.Method)).OrderByDescending(r => r.FinishedAt ?? r.CreatedAt).FirstOrDefault();
        if (pick is null)
        {
            return (null, none);
        }

        var full = await run.HandleAsync(new GetLabRunQuery(pick.Id), cancellationToken);
        return full.IsSuccess && full.Value.Verdict is { } verdict ? (verdict, string.Empty) : (null, none);
    }

    private async ValueTask<FaultState> CostSheetAsync(LabVerdictView verdict, CancellationToken cancellationToken)
    {
        if (verdict.CostStress is not { SheetId: { Length: > 0 } sheet } stress)
        {
            return FaultState.Unavailable;
        }

        var confirmedOn = await confirmation.ConfirmedOnAsync(sheet, cancellationToken);
        return stress.CostsChecked && confirmedOn is not null ? FaultState.Clear : FaultState.Fault;
    }

    private static RowStatus Word(string? status) => status switch
    {
        "pass" => RowStatus.Pass,
        "fail" => RowStatus.Fail,
        _ => RowStatus.Unavailable,
    };
}
