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

    public async ValueTask<Result<HoldOutResultView>> UnlockAsync(Guid runId, string mode, string actor, CancellationToken cancellationToken)
    {
        var run = await runs.GetAsync(runId, cancellationToken);
        if (run is null)
        {
            return Result.Failure<HoldOutResultView>(LabErrors.RunNotFound(runId));
        }

        var state = LabRunStates.Code(run.State);
        var verdict = run.State == LabRunState.Succeeded && LabSettings.RunsFullTest(run.Method) ? await results.GetAsync(run.Id, cancellationToken) : null;
        if (verdict is null)
        {
            return Result.Failure<HoldOutResultView>(LabErrors.NoReport(state, run.Method));
        }

        if (await store.GetUnlockAsync(run.StrategyId, run.Version, StrategyKind, cancellationToken) is not null)
        {
            return Result.Failure<HoldOutResultView>(LabErrors.HoldOutUsed(run.StrategyId, run.Version));
        }

        if (await BlockAsync(cancellationToken) is not { } block)
        {
            return Result.Failure<HoldOutResultView>(LabErrors.HoldOutUnavailable("The stored history is too short to keep a hold-out."));
        }

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

    /// <summary>The auto option (D6): runs once, recorded as <c>auto</c>, only when the option is on, nothing was looked at by this version, and every development row of the verdict is a pass. Returns null when it did not run.</summary>
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

        var result = await UnlockAsync(runId, AutoMode, "system:auto-unlock", cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }
}
