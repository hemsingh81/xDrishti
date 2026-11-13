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

/// <summary>What is stored with a result: both arms, the comparison, the rule it was judged by and the labels.</summary>
public sealed record SystemRunFigures(SystemArmFigures? Selection, SystemArmFigures? Baseline, ReplayArmResult? Comparison, SystemReplayPassRule Rule, IReadOnlyList<string> Labels, bool EngineChanged, string? Failure);

/// <summary>
/// Runs a claimed registration (SR-03 D4): the only code that builds a <see cref="HoldOutBlockReader"/>, and only from a <see cref="HoldOutClaim"/> made out of the stored fresh <c>system</c> look. Each arm runs in its own scope with the stored settings snapshot and strategy versions
/// (never today's) over <c>[block start, block end]</c>, in memory; nothing of the block's bars is written anywhere. The result (figures, verdict) is stored insert-only with the registration, behind the replay fence: no evidence reader reads it. A run that fails is stored
/// <c>inconclusive</c> with the reason, so the look is not retried; a cancel (host stopping) leaves the claim and the run resumes at the next start. Places no order.
/// </summary>
internal sealed class HoldOutSystemRunner(IServiceScopeFactory scopes, IHoldOutRegistrationStore store, IHoldOutStore blocks, ISimulationEngineVersion engine, TimeProvider time, ILogger<HoldOutSystemRunner> logger)
{
    private const int Look = 50;

    /// <summary>Runs every claimed registration that has no result yet, one at a time; returns how many were stored.</summary>
    public async ValueTask<int> RunPendingAsync(CancellationToken cancellationToken)
    {
        var done = 0;
        foreach (var registration in await store.ListAsync(Look, cancellationToken))
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
            return await StoreAsync(registration, claim, new SystemRunFigures(null, null, null, SystemReplayPassRule.Default, [], false, "the stored registration cannot be read"), SystemReplayPassRule.Default, null, cancellationToken);
        }

        SystemRunFigures figures;
        try
        {
            figures = await FiguresAsync(config, new HoldOutBlockReader(claim, config.BlockEnd), cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            HoldOutSystemLog.RunFailed(logger, registration.Id, failure.GetType().Name);
            figures = new SystemRunFigures(null, null, null, config.PassRule, [], false, "run_failed: " + failure.GetType().Name);
        }

        var verdict = figures.Failure is { } failed
            ? new SystemReplayVerdict(HoldOutStatus.Inconclusive, failed)
            : SystemReplayVerdict.Evaluate(config.PassRule, new SystemReplayFigures(figures.Selection?.ClosedTrades, figures.Selection?.NetBasis ?? false, figures.Selection?.CagrPct, figures.Selection?.MaxDrawdownPct, figures.Comparison?.AnswerCode));
        return await StoreAsync(registration, claim, figures, config.PassRule, verdict, cancellationToken, config);
    }

    private async ValueTask<SystemRunFigures> FiguresAsync(HoldOutRegistrationConfig config, HoldOutBlockReader reader, CancellationToken cancellationToken)
    {
        var versions = config.Versions.ToDictionary(p => p.Key, p => (IReadOnlyList<StoredVersion>)p.Value, StringComparer.Ordinal);
        var selection = await ArmAsync(config, versions, reader, ReplayArm.Selection, cancellationToken);
        if (selection.Failure is not null)
        {
            return new SystemRunFigures(null, null, null, config.PassRule, [], false, selection.Failure);
        }

        var baseline = await ArmAsync(config, versions, reader, ReplayArm.Baseline, cancellationToken);
        if (baseline.Failure is not null)
        {
            return new SystemRunFigures(selection.Figures, null, null, config.PassRule, selection.Labels, false, baseline.Failure);
        }

        var compared = ReplayPairComparison.CompareArms(selection.Series, baseline.Series, config.Parameters.Capital);
        var labels = selection.Labels.Union(baseline.Labels).Order(StringComparer.Ordinal).ToList();
        var changed = !string.Equals(engine.EngineVersion, config.EngineVersion, StringComparison.Ordinal);
        if (changed)
        {
            labels.Add("engine_changed");
        }

        return new SystemRunFigures(selection.Figures, baseline.Figures, compared.IsSuccess ? compared.Value : null, config.PassRule, labels, changed, compared.IsFailure ? "unavailable: the comparison could not be made (" + compared.Error.Code + ")" : null);
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
        return new ArmRun(figures, series, [.. labels], null);
    }

    private async ValueTask<bool> StoreAsync(StoredHoldOutRegistration registration, HoldOutClaim claim, SystemRunFigures figures, SystemReplayPassRule rule, SystemReplayVerdict? verdict, CancellationToken cancellationToken, HoldOutRegistrationConfig? config = null)
    {
        var used = verdict ?? SystemReplayVerdict.Evaluate(rule, new SystemReplayFigures(null, false, null, null, null));
        var stored = new StoredHoldOutResult(
            registration.Id, claim.Look.RunId, [HoldOutRegistrationIds.ArmId(registration.Id, "selection"), HoldOutRegistrationIds.ArmId(registration.Id, "baseline")], used.Status,
            ReplayJson.Canonical(new { verdict = used, figures }), string.Create(CultureInfo.InvariantCulture, $"registered={config?.EngineVersion ?? "unknown"};current={engine.EngineVersion}"), claim.Look.Mode, claim.Look.Actor, time.GetUtcNow());
        return await store.InsertResultAsync(stored, cancellationToken);
    }

    private sealed record ArmRun(SystemArmFigures? Figures, IReadOnlyList<ReplayDayEquity> Series, IReadOnlyList<string> Labels, string? Failure)
    {
        public static ArmRun Failed(string reason) => new(null, [], [], reason);
    }
}
