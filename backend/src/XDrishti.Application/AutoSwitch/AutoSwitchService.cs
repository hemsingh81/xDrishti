using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.AutoSwitch;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.SettingsCatalog;
using XDrishti.Application.Learning;
using XDrishti.Application.Learning.CellsRefresh;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Planning;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Application.SettingsCatalog;
using XDrishti.Domain.Common;

namespace XDrishti.Application.AutoSwitch;

/// <summary>
/// The one Auto switch (spec AU-17, ADR 0252). ON (owner, fresh password) checks the preconditions, resumes exactly the chain services that are paused, remembers which ones it resumed, stores <c>auto.enabled</c> and records the change; OFF (owner, session) pauses only the
/// services the last ON resumed and leaves every other choice alone. Nothing here touches autonomy, approval, arming or an order: the services it resumes are the data and planning chain, and the plan builder follows the stored switch.
/// </summary>
internal sealed partial class AutoSwitchService(
    IAutoSwitch state,
    IEssentialSettingStore settings,
    IServiceScheduleReader schedules,
    ServiceRegistry registry,
    ICommandHandler<ResumeServiceCommand, ServiceView> resume,
    ICommandHandler<PauseServiceCommand, ServiceView> pause,
    IAutoScopeStore scopes,
    IDataAccountStatus dataAccount,
    IChargeSheetConfirmationSource chargeSheet,
    IAutoSwitchLog log,
    IPlanStore plans,
    TimeProvider clock,
    ILogger<AutoSwitchService> logger,
    IPlanBuilderSettings? builderSettings = null)
{
    public const string Key = "auto.enabled";

    /// <summary>The services Auto resumes, in the order of the chain: data, history, the cell-statistics refresh (LC-01, ADR 0277), learning, the nightly plan, the suggestions.</summary>
    public static readonly IReadOnlyList<string> Chain = [EodService.Id, PortfolioBrokerHistoryService.Id, CellsRefreshService.Id, WeeklyService.Id, NightlyService.Id, SuggestionsService.Id];

    private const string Pass = "pass";
    private const string Fail = "fail";
    private const string Unavailable = "unavailable";

    public async ValueTask<AutoView> ReadAsync(CancellationToken cancellationToken)
    {
        var on = await state.IsOnAsync(cancellationToken);
        var changes = await log.RecentAsync(10, cancellationToken);
        var resumed = on ? changes.TakeWhile(c => c.Kind == AutoSwitchKinds.On).SelectMany(c => c.Changed).ToHashSet(StringComparer.Ordinal) : [];
        var chain = new List<AutoChainItemView>();
        foreach (var id in Chain)
        {
            var info = await schedules.GetAsync(id, cancellationToken);
            chain.Add(new AutoChainItemView(id, registry.NameOf(id), info?.Enabled == true, resumed.Contains(id)));
        }

        var paused = chain.Where(c => !c.Enabled).Select(c => c.Name).ToList();
        var complete = on && paused.Count == 0;
        var note = on && paused.Count > 0 ? "Auto is ON but the chain is incomplete: " + string.Join(", ", paused) + (paused.Count == 1 ? " is" : " are") + " paused or not available, so suggestions may not arrive." : null;
        var builder = on || builderSettings?.UseSelection == true ? SelectionPlanBuilder.BuilderId : NullPlanBuilder.BuilderId;
        return new AutoView(on, complete, note, builder, chain, await PreconditionsAsync(cancellationToken), await NightAsync(cancellationToken), [.. changes.Select(c => new AutoChangeView(c.Id, c.Kind, c.Actor, c.At, c.Changed, c.Snapshot))], AutoSwitchWording.NoOrderNotice);
    }

    public async ValueTask<Result<AutoView>> TurnOnAsync(TurnAutoOnCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!EssentialAudit.IsOwner(command.Actor))
        {
            return Result.Failure<AutoView>(AutoSwitchErrors.OwnerOnly);
        }

        if (!command.FreshPassword)
        {
            return Result.Failure<AutoView>(AutoSwitchErrors.PasswordRequired);
        }

        if (await state.IsOnAsync(cancellationToken))
        {
            return Result.Success(await ReadAsync(cancellationToken));
        }

        var preconditions = await PreconditionsAsync(cancellationToken);
        var blockers = preconditions.Where(p => p.Status != Pass).ToList();
        if (blockers.Count > 0)
        {
            return Result.Failure<AutoView>(AutoSwitchErrors.Refused(string.Join("; ", blockers.Select(b => $"{b.Label} ({b.Status}: {b.Detail})"))));
        }

        var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["auto.enabled.before"] = "false", ["auto.enabled.after"] = "true", ["builder.after"] = SelectionPlanBuilder.BuilderId };
        var resumed = new List<string>();
        foreach (var id in Chain)
        {
            var enabled = (await schedules.GetAsync(id, cancellationToken))!.Enabled;
            snapshot["service." + id + ".before"] = enabled ? "on" : "paused";
            if (enabled)
            {
                continue;
            }

            var done = await resume.HandleAsync(new ResumeServiceCommand(id, command.Actor, FreshPassword: true), cancellationToken);
            if (done.IsFailure)
            {
                await UndoAsync(resumed, command.Actor);
                return Result.Failure<AutoView>(AutoSwitchErrors.Failed($"{registry.NameOf(id)} could not be resumed ({done.Error.Code})."));
            }

            resumed.Add(id);
        }

        var now = clock.GetUtcNow();
        // shortcut: the setting (platform.app_settings, EF/other store) and the log row (planning schema, Npgsql) have no shared transaction; a failure after the services were resumed is compensated below.
        try
        {
            await settings.SetAsync(Key, "true", command.Actor, now, cancellationToken);
            await log.AppendAsync(AutoSwitchKinds.On, command.Actor, now, resumed, snapshot, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            await UndoAsync(resumed, command.Actor);
            await TryOffAsync(command.Actor);
            return Result.Failure<AutoView>(AutoSwitchErrors.Failed("the switch could not be stored."));
        }

        Switched(logger, AutoSwitchKinds.On, resumed.Count);
        return Result.Success(await ReadAsync(cancellationToken));
    }

    public async ValueTask<Result<AutoView>> TurnOffAsync(TurnAutoOffCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!EssentialAudit.IsOwner(command.Actor))
        {
            return Result.Failure<AutoView>(AutoSwitchErrors.OwnerOnly);
        }

        if (!await state.IsOnAsync(cancellationToken))
        {
            return Result.Success(await ReadAsync(cancellationToken));
        }

        // every ON since the last OFF (newest first), so two ONs that raced cannot leave the second one's services running; a service already paused is skipped below
        var resumedSinceOff = (await log.RecentAsync(50, cancellationToken)).TakeWhile(c => c.Kind == AutoSwitchKinds.On).SelectMany(c => c.Changed).ToHashSet(StringComparer.Ordinal);
        var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["auto.enabled.before"] = "true", ["auto.enabled.after"] = "false" };
        var paused = new List<string>();
        foreach (var id in Chain.Where(resumedSinceOff.Contains))
        {
            var enabled = (await schedules.GetAsync(id, cancellationToken))?.Enabled == true;
            snapshot["service." + id + ".before"] = enabled ? "on" : "paused";
            if (!enabled)
            {
                continue;
            }

            var done = await pause.HandleAsync(new PauseServiceCommand(id, command.Actor), cancellationToken);
            if (done.IsFailure)
            {
                // shortcut: no log row for a partial OFF. Auto stays ON and the services paused so far are already paused, so the next OFF (same ON rows, skips what is paused) finishes the job.
                return Result.Failure<AutoView>(Error.Conflict("auto.chain_failed", $"{registry.NameOf(id)} could not be paused ({done.Error.Code}); Auto stays ON, try again."));
            }

            paused.Add(id);
        }

        var now = clock.GetUtcNow();
        await settings.SetAsync(Key, "false", command.Actor, now, cancellationToken);
        await log.AppendAsync(AutoSwitchKinds.Off, command.Actor, now, paused, snapshot, cancellationToken);
        Switched(logger, AutoSwitchKinds.Off, paused.Count);
        return Result.Success(await ReadAsync(cancellationToken));
    }

    private async ValueTask<IReadOnlyList<AutoPreconditionView>> PreconditionsAsync(CancellationToken cancellationToken)
    {
        var list = new List<AutoPreconditionView>();
        list.Add(await CheckAsync("scope", "A scope (baskets or stocks) is set", async () =>
            await scopes.LatestAsync(cancellationToken) is { } scope ? (Pass, $"{scope.BasketIds.Count} basket(s), {scope.InstrumentIds.Count} stock(s)") : (Fail, "set a scope first (PUT /api/auto/scope)")));
        list.Add(await CheckAsync("data-account", "A Data account is connected", async () =>
        {
            var account = await dataAccount.GetAsync(cancellationToken);
            return account.CanFetch ? (Pass, account.Name ?? "connected") : (Fail, account.Reason ?? account.Status);
        }));
        list.Add(await CheckAsync("charge-sheet", "The charge sheet is confirmed (the chain needs costs)", async () =>
            await chargeSheet.FindAsync(cancellationToken) is not null ? (Pass, "confirmed") : (Fail, "confirm the charge sheet first")));
        list.Add(await CheckAsync("chain", "The chain services exist", async () =>
        {
            var missing = new List<string>();
            foreach (var id in Chain)
            {
                if (await schedules.GetAsync(id, cancellationToken) is null)
                {
                    missing.Add(id);
                }
            }

            return missing.Count == 0 ? (Pass, string.Join(", ", Chain)) : (Unavailable, "not registered: " + string.Join(", ", missing));
        }));
        return list;
    }

    /// <summary>A check that cannot be read is <c>unavailable</c>, which is never a pass.</summary>
    private static async Task<AutoPreconditionView> CheckAsync(string id, string label, Func<Task<(string Status, string Detail)>> check)
    {
        try
        {
            var (status, detail) = await check();
            return new AutoPreconditionView(id, label, status, detail);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            return new AutoPreconditionView(id, label, Unavailable, "could not be read (" + failure.GetType().Name + ")");
        }
    }

    private async ValueTask<AutoNightView> NightAsync(CancellationToken cancellationToken)
    {
        try
        {
            var runs = await plans.ListRunsAsync(null, 1, cancellationToken);
            return runs.Count == 0
                ? new AutoNightView("no_run", null, null, null)
                : new AutoNightView(runs[0].Run.Outcome, runs[0].Run.ForDate, runs[0].Run.FailedGate, runs[0].Run.Reason);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            return new AutoNightView(Unavailable, null, null, "the last run could not be read (" + failure.GetType().Name + ")");
        }
    }

    /// <summary>Puts back what a failed ON had resumed.</summary>
    private async ValueTask UndoAsync(List<string> resumed, string actor)
    {
        foreach (var id in resumed)
        {
            await pause.HandleAsync(new PauseServiceCommand(id, actor), CancellationToken.None);
        }
    }

    private async ValueTask TryOffAsync(string actor)
    {
        try
        {
            await settings.SetAsync(Key, "false", actor, clock.GetUtcNow(), CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            Switched(logger, "off-failed", 0);
        }
    }

    [LoggerMessage(EventId = 5601, Level = LogLevel.Information, Message = "auto.switch {Kind}: {Services} chain service(s) changed")]
    private static partial void Switched(ILogger logger, string kind, int services);
}
