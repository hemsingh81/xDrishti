using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Replay.Comparison;
using XDrishti.Domain.Replay.HoldOut;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>The figures of one arm over the block (net when the sheet charged every trade).</summary>
public sealed record SystemArmFigures(string Arm, int ClosedTrades, decimal Capital, decimal FinalEquity, decimal NetReturnPct, double? CagrPct, decimal? MaxDrawdownPct, bool NetBasis, string Basis, int Sessions);

/// <summary>What is stored with a result: both arms, the comparison, the rule it was judged by, the labels, the failure of a run that failed (the verdict is then <c>run_failed</c>) and the cell-statistics snapshot the arms started from (its keys and newest creation time).</summary>
public sealed record SystemRunFigures(SystemArmFigures? Selection, SystemArmFigures? Baseline, ReplayArmResult? Comparison, SystemReplayPassRule Rule, IReadOnlyList<string> Labels, bool EngineChanged, string? Failure, IReadOnlyList<string>? SnapshotKeys = null, DateTimeOffset? SnapshotCreatedAt = null);

/// <summary>
/// Runs a claimed registration (SR-03 D4): the only code that builds a <see cref="HoldOutBlockReader"/>, and only from a <see cref="HoldOutClaim"/> made out of the stored fresh <c>system</c> look, with the end of the <b>stored</b> block (never the registration's computed end). Each arm runs
/// in its own scope with the stored settings snapshot and strategy versions (never today's) over <c>[block start, block end]</c>, in memory; nothing of the block's bars is written anywhere. The result (figures, verdict) is stored insert-only with the registration, behind the replay fence: no
/// evidence reader reads it.
/// <para>
/// A failure of a database, a timeout or an I/O leaves the claim without a result and the next pass tries again, up to <see cref="HoldOutRunAttempts.Max"/> times; then, and at once for any other failure (a replay error, a defect), the result is stored <c>run_failed</c>
/// with the reason, so the look is never retried for ever. <c>inconclusive</c> stays for an unavailable figure of the rule. A stop (cancel) leaves the claim and the run resumes at the next start. If the stored block no longer ends where the registration said, nothing is
/// read or stored. Places no order.
/// </para>
/// </summary>
internal sealed class HoldOutSystemRunner(IServiceScopeFactory scopes, IHoldOutRegistrationStore store, IHoldOutStore blocks, ITestLedger ledger, ISimulationEngineVersion engine, HoldOutRunAttempts attempts, TimeProvider time, ILogger<HoldOutSystemRunner> logger)
{
    private const int Newest = 50;

    // shortcut: the count of transient failures is kept in memory (HoldOutRunAttempts), so a host that restarts between tries gives the run more tries; persist an insert-only attempts row before each read if that ever matters

    /// <summary>Runs every claimed registration that has no result yet, one at a time; returns how many results were stored.</summary>
    public async ValueTask<int> RunPendingAsync(CancellationToken cancellationToken)
    {
        var done = 0;
        foreach (var registration in await store.ListAsync(Newest, cancellationToken))
        {
            if (await store.GetResultAsync(registration.Id, cancellationToken) is not null)
            {
                continue;
            }

            var look = await blocks.GetUnlockAsync(HoldOutRegistrationIds.LookKey(registration.Id), HoldOutRegistrationIds.LookVersion, HoldOutHorizon.SystemKind, registration.BlockStart, cancellationToken);
            if (look is null || HoldOutClaim.From(look, registration.Id) is not { IsSuccess: true } claim)
            {
                continue;
            }

            if (await RunAsync(registration, claim.Value, cancellationToken))
            {
                done++;
            }
        }

        return done;
    }

    private async ValueTask<bool> RunAsync(StoredHoldOutRegistration registration, HoldOutClaim claim, CancellationToken cancellationToken)
    {
        var config = ReplayJson.Read<HoldOutRegistrationConfig>(registration.ConfigJson);
        if (config is null)
        {
            return await StoreAsync(registration, claim, Failed(SystemReplayPassRule.Default, "the stored registration cannot be read"), null, cancellationToken);
        }

        // the reader's end is the stored block's, never the registration's computed one: a changed BlockMonths must not let the run read past the block
        var block = (await blocks.ListBlocksAsync(cancellationToken)).Select(b => b.Block).FirstOrDefault(b => b.Start == registration.BlockStart);
        if (block is null || block.HistoryLast != config.BlockEnd)
        {
            // nothing was read; the look is spent and can never be taken again, so the result says so once instead of this pass finding it again every poll
            HoldOutSystemLog.BlockChanged(logger, registration.Id);
            return await StoreAsync(registration, claim, Failed(config.PassRule, "run_failed: the stored block no longer ends where the registration said; nothing was read"), null, cancellationToken, config);
        }

        SystemRunFigures figures;
        try
        {
            // the ledger rows first: no read of the block without its trial rows (a failure here is a database failure and is retried)
            await HoldOutLedgerRows.AppendAsync(ledger, registration.Id, config, time.GetUtcNow(), cancellationToken);
            figures = await FiguresAsync(config, new HoldOutBlockReader(claim, block.HistoryLast), cancellationToken);
        }
        catch (Exception failure) when (IsTransient(failure))
        {
            var count = attempts.Add(registration.Id);
            HoldOutSystemLog.RunRetry(logger, registration.Id, Root(failure).GetType().Name, count);
            if (count < HoldOutRunAttempts.Max)
            {
                return false; // the claim stays; the next pass tries again
            }

            figures = Failed(config.PassRule, $"run_failed: {Root(failure).GetType().Name} on {count.ToString(CultureInfo.InvariantCulture)} tries");
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            HoldOutSystemLog.RunFailed(logger, registration.Id, failure.GetType().Name);
            figures = Failed(config.PassRule, "run_failed: " + failure.GetType().Name);
        }

        var verdict = figures.Failure is { } failed
            ? new SystemReplayVerdict(SystemReplayVerdict.RunFailedStatus, failed)
            : SystemReplayVerdict.Evaluate(config.PassRule, new SystemReplayFigures(figures.Selection?.ClosedTrades, figures.Selection?.NetBasis ?? false, figures.Selection?.CagrPct, figures.Selection?.MaxDrawdownPct, figures.Comparison?.AnswerCode));
        return await StoreAsync(registration, claim, figures, verdict, cancellationToken, config);
    }

    /// <summary>A database, timeout or I/O failure anywhere in the chain of inner exceptions: EF's retrying strategy wraps the provider's failure in a <c>RetryLimitExceededException</c>, so the outer type says nothing.</summary>
    private static bool IsTransient(Exception failure)
    {
        for (Exception? e = failure; e is not null; e = e.InnerException)
        {
            if (e is OperationCanceledException or OutOfMemoryException)
            {
                return false;
            }

            if (e is DbException or TimeoutException or IOException)
            {
                return true;
            }
        }

        return false;
    }

    private static Exception Root(Exception failure)
    {
        var e = failure;
        while (e.InnerException is { } inner)
        {
            e = inner;
        }

        return e;
    }

    private static SystemRunFigures Failed(SystemReplayPassRule rule, string reason) => new(null, null, null, rule, [], false, reason);

    private async ValueTask<SystemRunFigures> FiguresAsync(HoldOutRegistrationConfig config, HoldOutBlockReader reader, CancellationToken cancellationToken)
    {
        var versions = config.Versions.ToDictionary(p => p.Key, p => (IReadOnlyList<StoredVersion>)p.Value, StringComparer.Ordinal);
        var selection = await ArmAsync(config, versions, reader, ReplayArm.Selection, cancellationToken);
        if (selection.Failure is not null)
        {
            return Failed(config.PassRule, selection.Failure);
        }

        var baseline = await ArmAsync(config, versions, reader, ReplayArm.Baseline, cancellationToken);
        if (baseline.Failure is not null)
        {
            return Failed(config.PassRule, baseline.Failure);
        }

        // an arm comparison that cannot be made is an unavailable figure of the rule (inconclusive), not a failed run
        var compared = ReplayPairComparison.CompareArms(selection.Series, baseline.Series, config.Parameters.Capital);
        var labels = selection.Labels.Union(baseline.Labels).Order(StringComparer.Ordinal).ToList();
        var changed = !string.Equals(engine.EngineVersion, config.EngineVersion, StringComparison.Ordinal);
        if (changed)
        {
            labels.Add("engine_changed");
        }

        return new SystemRunFigures(selection.Figures, baseline.Figures, compared.IsSuccess ? compared.Value : null, config.PassRule, labels, changed, null, selection.SnapshotKeys, selection.SnapshotCreatedAt);
    }

    private async ValueTask<ArmRun> ArmAsync(HoldOutRegistrationConfig config, IReadOnlyDictionary<string, IReadOnlyList<StoredVersion>> versions, HoldOutBlockReader reader, ReplayArm arm, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var facts = scope.ServiceProvider.GetRequiredService<IReplayFacts>();
        if (facts is not ReplayFacts block)
        {
            return ArmRun.Failed("the replay facts cannot read a hold-out block");
        }

        block.UseBlock(reader);
        var driver = scope.ServiceProvider.GetRequiredService<ReplayDriver>();
        var parameters = config.Parameters with { Arm = arm, Mode = ReplayMode.Frozen };
        var prepared = await driver.PrepareAsync(parameters, config.Settings, versions, cancellationToken);
        if (prepared.IsFailure)
        {
            return ArmRun.Failed(prepared.Error.Code + ": " + prepared.Error.Message);
        }

        var cells = (await facts.CellsBeforeStartAsync(parameters, cancellationToken)).Cells;
        var keys = cells.Select(c => c.Stored.SnapshotKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        DateTimeOffset? created = cells.Count == 0 ? null : cells.Max(c => c.Stored.CreatedAt);
        var series = new List<ReplayDayEquity>();
        var labels = new SortedSet<string>(HindsightLabels.For(parameters), StringComparer.Ordinal);
        foreach (var day in prepared.Value.Sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await driver.StepAsync(prepared.Value, day, cancellationToken);
            if (record.IsFailure)
            {
                return ArmRun.Failed(record.Error.Code + ": " + record.Error.Message);
            }

            series.Add(new ReplayDayEquity(day, record.Value.Equity));
            labels.UnionWith(record.Value.Labels);
        }

        var final = prepared.Value.Book.FiguresAsOf(parameters.To);
        var equity = final.Equity ?? parameters.Capital;
        var years = (parameters.To.DayNumber - parameters.From.DayNumber + 1) / 365.25;
        double? cagr = years <= 0 || equity <= 0 ? null : (Math.Pow((double)(equity / parameters.Capital), 1 / years) - 1) * 100;
        var figures = new SystemArmFigures(
            arm == ReplayArm.Baseline ? "baseline" : "selection", final.ClosedCount, parameters.Capital, equity, Math.Round((equity - parameters.Capital) / parameters.Capital * 100, 4), cagr, final.MaxDrawdownPct,
            string.Equals(final.Basis, PaperWording.AfterStoredSheet, StringComparison.Ordinal), final.Basis, series.Count);
        return new ArmRun(figures, series, [.. labels], null, keys, created);
    }

    private async ValueTask<bool> StoreAsync(StoredHoldOutRegistration registration, HoldOutClaim claim, SystemRunFigures figures, SystemReplayVerdict? verdict, CancellationToken cancellationToken, HoldOutRegistrationConfig? config = null)
    {
        var used = verdict ?? new SystemReplayVerdict(SystemReplayVerdict.RunFailedStatus, figures.Failure ?? "run_failed");
        var stored = new StoredHoldOutResult(
            registration.Id, claim.Look.RunId, [HoldOutRegistrationIds.ArmId(registration.Id, "selection"), HoldOutRegistrationIds.ArmId(registration.Id, "baseline")], used.Status,
            ReplayJson.Canonical(new { verdict = used, figures }), string.Create(CultureInfo.InvariantCulture, $"registered={config?.EngineVersion ?? "unknown"};current={engine.EngineVersion}"), claim.Look.Mode, claim.Look.Actor, time.GetUtcNow());
        var inserted = await store.InsertResultAsync(stored, cancellationToken);
        attempts.Clear(registration.Id);
        return inserted;
    }

    private sealed record ArmRun(SystemArmFigures? Figures, IReadOnlyList<ReplayDayEquity> Series, IReadOnlyList<string> Labels, string? Failure, IReadOnlyList<string>? SnapshotKeys = null, DateTimeOffset? SnapshotCreatedAt = null)
    {
        public static ArmRun Failed(string reason) => new(null, [], [], reason);
    }
}
