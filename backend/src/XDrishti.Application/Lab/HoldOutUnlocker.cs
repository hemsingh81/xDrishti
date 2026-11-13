using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>
/// The one look at the hold-out of a strategy version (spec CF-06, D5 and D6): runs the frozen plan once over the block, judges it with the rule and stores the insert-only unlock row. The first look at the block is <c>fresh</c>; every later one, by
/// another version or kind, is <c>reused_window</c> and cannot count toward a pass. A second look by the same version is refused. The owner act authenticates before this is called; the auto path calls it only when every development row is a pass.
/// </summary>
internal sealed class HoldOutUnlocker(
    ILabRunStore runs,
    ILabResultStore results,
    IHoldOutStore store,
    IPlanFreezeStore freezes,
    IHoldOutPass pass,
    LabHoldOutGuard guard,
    HoldOutOptions options,
    TimeProvider time,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    ILogger<HoldOutUnlocker> logger)
{
    public const string StrategyKind = "strategy";
    public const string OwnerMode = "owner";
    public const string AutoMode = "auto";

    public const string NoFrozenPlan = "This run has no frozen plan; post a full run (same explicit window, or a new run) and unlock that run.";

    /// <summary>
    /// The cheap preconditions of a look, shared by the endpoint (before it queues the job) and the job (before it runs the pass): the run exists and succeeded as a full or robust test with a stored verdict, its version has not looked yet,
    /// the plan is frozen and the stored history can keep a block. Nothing here reads a session of the hold-out.
    /// </summary>
    public async ValueTask<Result<UnlockPlan>> CheckAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await runs.GetAsync(runId, cancellationToken);
        if (run is null)
        {
            return Result.Failure<UnlockPlan>(LabErrors.RunNotFound(runId));
        }

        var state = LabRunStates.Code(run.State);
        var verdict = run.State == LabRunState.Succeeded && LabSettings.RunsFullTest(run.Method) ? await results.GetAsync(run.Id, cancellationToken) : null;
        if (verdict is null)
        {
            return Result.Failure<UnlockPlan>(LabErrors.NoReport(state, run.Method));
        }

        // a what-if run (conservative slippage, stress changed from the method's default, nulls left out or an own number of seeds) never spends the version's single look and never sets scorecard row 12
        if (LabRunOptions.Parse(run.RunOptions).KeyText(string.Equals(run.Method, LabSettings.RobustMethod, StringComparison.Ordinal)) is not null)
        {
            return Result.Failure<UnlockPlan>(LabErrors.HoldOutUnavailable("This run was posted with non-default options (conservative slippage, a changed stress setting, `nulls: false` or its own `draws`); it is a what-if and cannot take the hold-out. Unlock a run posted with the default options."));
        }

        if (await store.GetUnlockAsync(run.StrategyId, run.Version, StrategyKind, cancellationToken) is not null)
        {
            return Result.Failure<UnlockPlan>(LabErrors.HoldOutUsed(run.StrategyId, run.Version));
        }

        if (await BlockAsync(cancellationToken) is not { } block)
        {
            return Result.Failure<UnlockPlan>(LabErrors.HoldOutUnavailable("The stored history is too short to keep a hold-out."));
        }

        return await freezes.FirstAsync(run.StrategyId, run.Version, cancellationToken) is null
            ? Result.Failure<UnlockPlan>(LabErrors.HoldOutUnavailable(NoFrozenPlan))
            : Result.Success(new UnlockPlan(run, verdict, block));
    }

    public async ValueTask<Result<HoldOutResultView>> UnlockAsync(Guid runId, string mode, string actor, CancellationToken cancellationToken)
    {
        var plan = await CheckAsync(runId, cancellationToken);
        if (plan.IsFailure)
        {
            return Result.Failure<HoldOutResultView>(plan.Error);
        }

        var (run, verdict, block) = plan.Value;
        var figures = await pass.RunAsync(run, verdict, block, cancellationToken);
        if (figures.IsFailure)
        {
            return Result.Failure<HoldOutResultView>(figures.Error);
        }

        // fresh is decided by the database: a partial unique index admits one non-reused look per block, so a look that loses the race is stored again as re-used (the figures are the same; only the flag and the verdict of it change)
        var reused = await store.AnyUnlockAsync(block.Start, cancellationToken);
        var planHash = await freezes.FirstAsync(run.StrategyId, run.Version, cancellationToken);
        var now = time.GetUtcNow();
        var view = Judge(run, verdict, figures.Value, mode, actor, reused, planHash, now);
        var stored = await store.InsertUnlockAsync(new StoredUnlock(run.StrategyId, run.Version, StrategyKind, mode, actor, now, view.SettingHash, reused, block.Start, run.Id, view), cancellationToken);
        if (!stored && !reused && await store.GetUnlockAsync(run.StrategyId, run.Version, StrategyKind, cancellationToken) is null)
        {
            reused = true;
            view = Judge(run, verdict, figures.Value, mode, actor, reused, planHash, now);
            stored = await store.InsertUnlockAsync(new StoredUnlock(run.StrategyId, run.Version, StrategyKind, mode, actor, now, view.SettingHash, reused, block.Start, run.Id, view), cancellationToken);
        }

        if (!stored)
        {
            return Result.Failure<HoldOutResultView>(LabErrors.HoldOutUsed(run.StrategyId, run.Version));
        }

        await AuditAsync(run, view, now);
        return Result.Success(view);
    }

    private async ValueTask<HoldOutBlock?> BlockAsync(CancellationToken cancellationToken) =>
        await guard.HistoryAsync(cancellationToken) is { } history ? await guard.EnsureAsync(history.First, history.Last, cancellationToken) : null;

    private HoldOutResultView Judge(LabRun run, LabVerdictView verdict, HoldOutFigures figures, string mode, string actor, bool reused, string? planHash, DateTimeOffset now)
    {
        var sawIt = run.To >= figures.From;
        var counts = !reused && !sawIt;
        var notCounted = reused ? "the block was already looked at" : "the run's window reached the hold-out";
        var dev = verdict.Evidence.ExpectancyR ?? double.NaN;
        var hold = figures.MeanR ?? double.NaN;
        var judged = HoldOutRule.Evaluate(dev, hold, figures.Trades, options.MinTrades, options.Retention, counts, notCounted);
        return new HoldOutResultView(
            run.StrategyId,
            run.Version,
            StrategyKind,
            mode,
            reused ? "reused" : "used",
            now,
            figures.From,
            figures.To,
            reused,
            sawIt,
            judged.Passed,
            judged.Status,
            judged.Reason,
            double.IsFinite(dev) ? dev : null,
            double.IsFinite(hold) ? hold : null,
            judged.Ratio is { } ratio && double.IsFinite(ratio) ? ratio : null,
            figures.Trades,
            planHash,
            options.SnapshotHash(),
            actor,
            reused ? ["Re-used window: the numbers are shown, the result counts as a trial and cannot count toward a pass; only forward data can refresh the evidence."] : []);
    }

    /// <summary>The audit cross-index row (as P7-12): ids and state words only, the mode as <c>auto</c> or <c>owner</c> and never a number of the evidence. A failure of this second write is logged and does not undo the look, which is insert-only.</summary>
    private async ValueTask AuditAsync(LabRun run, HoldOutResultView view, DateTimeOffset now)
    {
        // shortcut: the look is the commit point and the audit row is best effort; a miss is logged (HoldOutAuditFailed) and not repaired, because a second job of the version is refused as lab.holdout_used before it reaches here
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.LabHoldOutUnlocked, null, string.Create(CultureInfo.InvariantCulture, $"run={run.Id:D} strategy={run.StrategyId} v={run.Version} mode={view.Mode} state={view.State} reused={(view.ReusedWindow ? "yes" : "no")}"), view.Actor, now));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            LabRunLog.HoldOutAuditFailed(logger, run.Id, failure.GetType().Name);
        }
    }

    /// <summary>The auto option (D6): runs once, recorded as <c>auto</c>, only when the option is on, nothing was looked at by this version, and every development row of the verdict is a pass, and never at block 1 of the chain (HO-01). Returns null when it did not run.</summary>
    public async ValueTask<HoldOutResultView?> TryAutoAsync(Guid runId, LabVerdictView verdict, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        if (!options.AutoUnlock || !HoldOutAutoGate.DevelopmentPass(verdict))
        {
            return null;
        }

        // window_clear: a run whose window reached the block cannot pass, so it must not burn the block's one fresh look
        var run = await runs.GetAsync(runId, cancellationToken);
        var block = run is null ? null : await BlockAsync(cancellationToken);
        if (run is null || !HoldOutAutoGate.AllPass(verdict, run.To, block?.Start))
        {
            return null;
        }

        // HO-01 (coordinator, 2026-11-13): the auto look never claims block 1. It has no pre-dating horizon and its look is irreversible, so it stays the owner's act (a manual unlock, or a release with the forfeit). Automatic looks start at block 2.
        if (block is { PrevStart: null })
        {
            return null;
        }

        var result = await UnlockAsync(runId, AutoMode, "system:auto-unlock", cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }
}

/// <summary>What the cheap checks of a look found: the run, its stored verdict and the locked block.</summary>
internal sealed record UnlockPlan(LabRun Run, LabVerdictView Verdict, HoldOutBlock Block);
