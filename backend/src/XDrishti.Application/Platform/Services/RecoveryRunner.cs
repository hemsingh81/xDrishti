using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// Makes up what the machine missed, once and in order (ADR 0240, spec AU-02). A trigger (start-up, a scheduled time found late, a successful Data-account connect) asks for a pass; a request inside
/// <c>Recovery:DebounceMinutes</c> of the last accepted one, or while a pass runs, starts nothing and records nothing. A pass plans with <see cref="RecoveryPlanner"/>, then runs the steps one at a time through
/// <see cref="ServiceRunner"/> with the trigger <c>catch_up</c> (so a step has the normal retries, timeout and run record) and writes one ledger item per service and occurrence: a step that already succeeded is never
/// started again, an item left <c>running</c> by an earlier process becomes failed (interrupted) and is eligible again, a failure blocks the services that follow it (and only those), a service that must not run in
/// market hours waits (<c>waiting: market hours</c>) and a later pass resumes it after the close. A failed or blocked step is tried again at the next trigger, at most <c>Recovery:MaxAttempts</c> times. The runner never
/// replays a decision-time capture (the planner reports it missed), never touches an order and never skips a service's own fail-closed checks: it only starts the service.
/// A service that was never run, or was paused for a long time, has the baseline of its last run or of its schedule row (a first start, a resume or an edit), so days before it existed are not reported as missed and
/// the first enablement does not cause a surprise run.
/// </summary>
public sealed class RecoveryRunner(
    IServiceScopeFactory scopes,
    ServiceRegistry registry,
    ServiceRunner runner,
    IApplicationInfo application,
    TimeProvider clock,
    RecoverySettings options,
    ILogger<RecoveryRunner> logger) : IRecoveryTrigger, IDisposable
{
    /// <summary>Recorded as the requester of the runs a pass starts.</summary>
    public const string Requester = "system:recovery";

    public const string WaitingMarketHours = "waiting: market hours";

    private readonly CancellationTokenSource _stopping = new();
    private readonly object _gate = new();
    private Task? _pass;
    private DateTimeOffset? _lastAccepted;
    private DateTimeOffset? _resumeAt;
    private RecoveryTrigger? _pending;

    /// <summary>The instant a step waiting for the market hours to end can go on; null when none waits. The scheduler asks for a pass with <see cref="RecoveryTrigger.Resume"/> when it has come.</summary>
    public DateTimeOffset? ResumeAt
    {
        get
        {
            lock (_gate)
            {
                return _resumeAt;
            }
        }
    }

    /// <inheritdoc />
    public bool Request(RecoveryTrigger trigger)
    {
        var code = RecoveryTriggerCodes.Code(trigger);
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            if (_stopping.IsCancellationRequested)
            {
                return false;
            }

            if (_pass is { IsCompleted: false })
            {
                _pending = trigger == RecoveryTrigger.Resume ? _pending : trigger; // a trigger refused for now is not lost: one trailing pass follows (no ledger row for it)
                ServicesLog.RecoveryIgnored(logger, code, "a pass is running");
                return false;
            }

            var debounce = TimeSpan.FromMinutes(Math.Clamp(options.DebounceMinutes, 1, 120));
            if (trigger != RecoveryTrigger.Resume && _lastAccepted is { } last && now >= last && now - last < debounce)
            {
                _pending = trigger;
                ServicesLog.RecoveryIgnored(logger, code, "inside the debounce");
                return false;
            }

            _lastAccepted = now;
            _resumeAt = null;
            _pending = null;
            _pass = Task.Run(() => PassAsync(trigger, now), CancellationToken.None);
            return true;
        }
    }

    /// <summary>Asks for the trailing pass of a trigger that was refused (a pass was running or the debounce was on), and for the pass a step waiting for the close of the market hours needs, when that time has come. Cheap: no read.</summary>
    public void ResumeIfDue()
    {
        RecoveryTrigger? pending;
        lock (_gate)
        {
            pending = _pending;
        }

        if (pending is { } trailing)
        {
            Request(trailing); // accepted once the pass has ended and the debounce is over; refused (and kept) until then
        }

        if (ResumeAt is { } at && clock.GetUtcNow() >= at)
        {
            Request(RecoveryTrigger.Resume);
        }
    }

    /// <summary>Completes when no pass is running. For tests and for shutdown.</summary>
    public Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return _pass ?? Task.CompletedTask;
        }
    }

    /// <summary>Tells the pass that the application is stopping and waits for it, at most <paramref name="wait"/>. What it left running is closed as interrupted by the next pass.</summary>
    public async Task StopAsync(TimeSpan wait)
    {
        await _stopping.CancelAsync();
        try
        {
            await WhenIdleAsync().WaitAsync(wait, clock);
        }
        catch (TimeoutException)
        {
            // the pass did not end in time
        }
    }

    public void Dispose() => _stopping.Dispose();

    private async Task PassAsync(RecoveryTrigger trigger, DateTimeOffset now)
    {
        var code = RecoveryTriggerCodes.Code(trigger);
        var token = _stopping.Token;
        Guid? passId = null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var ledger = scope.ServiceProvider.GetRequiredService<IRecoveryLedger>();
            await ledger.InterruptRunningAsync(now, token);
            if (await PlanAsync(scope.ServiceProvider, now, token) is not { } planned)
            {
                return;
            }

            if (planned.Plan.Steps.Count == 0)
            {
                ServicesLog.RecoveryFinished(logger, code, "nothing_to_do");
                return;
            }

            passId = await ledger.BeginPassAsync(code, now, token);
            ServicesLog.RecoveryStarted(logger, code, planned.Plan.Steps.Count);
            var status = await RunStepsAsync(scope.ServiceProvider, ledger, passId.Value, planned, token);
            await ledger.EndPassAsync(passId.Value, status, clock.GetUtcNow(), CancellationToken.None);
            ServicesLog.RecoveryFinished(logger, code, status);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await EndQuietlyAsync(passId, "interrupted");
        }
#pragma warning disable CA1031 // a failed pass must not end the scheduler; the next trigger tries again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.RecoveryCrashed(logger, code, ex);
            await EndQuietlyAsync(passId, "failed");
        }
    }

    private async Task EndQuietlyAsync(Guid? passId, string status)
    {
        if (passId is null)
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IRecoveryLedger>().EndPassAsync(passId.Value, status, clock.GetUtcNow(), CancellationToken.None);
        }
#pragma warning disable CA1031 // the next pass closes it as interrupted
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.RecoveryCrashed(logger, status, ex);
        }
    }

    private sealed record Planned(RecoveryPlan Plan, IReadOnlyDictionary<string, RecoveryInput> Inputs, TradingCalendar Calendar);

    private async Task<Planned?> PlanAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var all = await services.GetRequiredService<ServiceSettingsReader>().ReadAllAsync(cancellationToken);
        var latest = await services.GetRequiredService<IServiceRunStore>().GetLatestAsync(registry.Ids, cancellationToken);
        var calendar = await services.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
        var retryable = (await services.GetRequiredService<IRecoveryLedger>().ListRetryableAsync(options.MaxAttempts, cancellationToken))
            .GroupBy(i => i.ServiceId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Min(i => i.SessionFrom), StringComparer.Ordinal);
        var inputs = all.Where(s => !registry.IsExternal(s.Id)).Select(s =>
        {
            var baseline = Baseline(s, latest.GetValueOrDefault(s.Id), application.StartedAt);
            if (retryable.TryGetValue(s.Id, out var from))
            {
                // a failed step is tried again at the next trigger: the plan counts from the start of its session, although the failed run is a run (ADR 0240, AU-02)
                var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
                baseline = start < baseline ? start : baseline;
            }

            return new RecoveryInput(s.Definition, s.Settings, baseline);
        }).ToList();
        var result = RecoveryPlanner.Plan(inputs, calendar, now, options.MaxSessions);
        if (result.IsFailure)
        {
            ServicesLog.RecoveryPlanRefused(logger, result.Error.Code);
            return null;
        }

        return new Planned(result.Value, inputs.ToDictionary(i => i.Definition.Id, StringComparer.Ordinal), calendar);
    }

    /// <summary>
    /// The instant up to which a service's days are taken as dealt with: the later of its latest run and its schedule row (a resume, an edit, or the first-start row of a service that never ran), so a service paused for a long time, or enabled today, is not
    /// reported as missed for days before it counted. A service with neither (no row, never run) counts from the start of this process: nothing is missed at start-up, and a later wake finds what it missed since.
    /// </summary>
    internal static DateTimeOffset Baseline(EffectiveService service, ServiceRun? last, DateTimeOffset processStart)
    {
        // the first-start row is no decision of the owner: it is the registration of a service that never ran, and no more once a run exists
        DateTimeOffset? row = service.Row is { } saved && (last is null || saved.UpdatedBy != ServiceScheduler.SeedRequester) ? saved.UpdatedAt : null;
        return (last?.StartedAt, row) switch
        {
            (null, null) => processStart,
            (null, var r) => r!.Value,
            (var l, null) => l!.Value,
            var (l, r) => l > r ? l!.Value : r!.Value,
        };
    }

    private async Task<string> RunStepsAsync(IServiceProvider services, IRecoveryLedger ledger, Guid passId, Planned planned, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Clamp(options.MaxAttempts, 1, 10);
        var registered = planned.Inputs.Keys.ToHashSet(StringComparer.Ordinal);
        var failed = new Dictionary<string, string>(StringComparer.Ordinal); // service -> why what follows it cannot run
        var waiting = new HashSet<string>(StringComparer.Ordinal);
        var allGood = true;
        foreach (var step in planned.Plan.Steps)
        {
            var id = step.ServiceId;
            var occurrence = step.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var existing = await ledger.FindAsync(id, occurrence, cancellationToken);
            if (existing is { State: RecoveryStates.Succeeded or RecoveryStates.Superseded })
            {
                continue;
            }

            if (existing is { State: RecoveryStates.Failed } && existing.Attempts >= maxAttempts)
            {
                failed[id] = existing.Reason ?? "failed";
                allGood = false;
                continue; // stays failed (never overwritten): the notice is derived from the ledger
            }

            var item = existing ?? new RecoveryItemRow(Guid.CreateVersion7(clock.GetUtcNow()), passId, id, occurrence, step.From, step.To, RecoveryStates.Running, null, 0, null);
            item = item with { PassId = passId, SessionFrom = step.From, SessionTo = step.To };
            var deps = RecoveryPlanner.DependenciesOf(planned.Inputs[id], registered);
            var blocker = deps.FirstOrDefault(d => failed.ContainsKey(d) || waiting.Contains(d));
            if (blocker is null)
            {
                // a service this one follows that failed in an earlier pass, used up its attempts and so is not in this plan, still blocks it
                foreach (var d in deps)
                {
                    if (await ledger.FindNewestAsync(d, cancellationToken) is { State: RecoveryStates.Failed or RecoveryStates.Blocked } earlier && earlier.SessionTo >= step.From)
                    {
                        failed[d] = earlier.Reason ?? "failed";
                        blocker = d;
                        break;
                    }
                }
            }

            if (blocker is not null)
            {
                allGood = false;
                if (waiting.Contains(blocker))
                {
                    waiting.Add(id);
                    await ledger.SaveAsync(item with { State = RecoveryStates.Waiting, Reason = Clip($"waiting: {blocker} waits") }, cancellationToken);
                    ServicesLog.RecoveryStep(logger, id, occurrence, RecoveryStates.Waiting);
                }
                else
                {
                    failed[id] = $"{blocker} did not run";
                    await ledger.SaveAsync(item with { State = RecoveryStates.Blocked, Reason = Clip($"blocked by {blocker}: {failed[blocker]}") }, cancellationToken);
                    ServicesLog.RecoveryBlocked(logger, id, occurrence, blocker);
                }

                continue;
            }

            // the settings are read again for each step: a service paused or with catch-up switched off while the pass runs is not started (paused stays paused), and the nightly's cutoff is checked again
            if (!await StillEligibleAsync(services, planned, step, cancellationToken))
            {
                failed[id] = "no longer eligible (paused, catch-up off or past its cutoff)";
                allGood = false;
                continue;
            }

            if (InMarketHours(planned.Inputs[id], planned.Calendar) is { } resumeAt)
            {
                waiting.Add(id);
                allGood = false;
                lock (_gate)
                {
                    _resumeAt = _resumeAt is { } current && current < resumeAt ? current : resumeAt;
                }

                await ledger.SaveAsync(item with { State = RecoveryStates.Waiting, Reason = WaitingMarketHours }, cancellationToken);
                ServicesLog.RecoveryStep(logger, id, occurrence, RecoveryStates.Waiting);
                continue;
            }

            await ledger.SupersedeOlderAsync(id, occurrence, cancellationToken); // an older open item of the service is covered by this step, so it no longer holds the baseline back
            var done = await RunStepAsync(services, ledger, planned, step, item, cancellationToken);
            if (done.State == RecoveryStates.Waiting)
            {
                waiting.Add(id);
                allGood = false;
            }
            else if (done.State != RecoveryStates.Succeeded)
            {
                failed[id] = done.Reason ?? "failed";
                allGood = false;
            }
        }

        return allGood ? "succeeded" : "incomplete";
    }

    private async Task<bool> StillEligibleAsync(IServiceProvider services, Planned planned, RecoveryStep step, CancellationToken cancellationToken)
    {
        var current = await services.GetRequiredService<ServiceSettingsReader>().ReadAsync(step.ServiceId, cancellationToken);
        if (!current.Settings.Enabled || !current.Settings.CatchUp)
        {
            return false;
        }

        if (!string.Equals(step.ServiceId, RecoveryPlanner.NightlyId, StringComparison.Ordinal))
        {
            return true;
        }

        // the nightly runs only before its review cutoff: plan again at this instant and keep the step only if it is still there
        return await PlanAsync(services, clock.GetUtcNow(), cancellationToken) is { } again && again.Plan.Steps.Any(s => s.ServiceId == step.ServiceId && s.To == step.To);
    }

    private async Task<RecoveryItemRow> RunStepAsync(IServiceProvider services, IRecoveryLedger ledger, Planned planned, RecoveryStep step, RecoveryItemRow item, CancellationToken cancellationToken)
    {
        var id = step.ServiceId;
        var runs = services.GetRequiredService<IServiceRunStore>();
        var due = planned.Plan.Items.Where(i => i.ServiceId == id && i.Status == RecoveryItemStatus.Planned).Max(i => i.Due);

        // a run of the scheduler may be going: it is waited for, and when it made up the occurrence the step is done (one run per occurrence, never two)
        await runner.WhenServiceIdleAsync(id, cancellationToken);
        if ((await runs.GetLatestAsync([id], cancellationToken)).GetValueOrDefault(id) is { } handled && handled.StartedAt >= due && handled.Status != ServiceRunStatus.Running)
        {
            if (handled.Status == ServiceRunStatus.Succeeded)
            {
                var made = item with { State = RecoveryStates.Succeeded, Reason = null, RunId = handled.Id };
                await ledger.SaveAsync(made, cancellationToken);
                ServicesLog.RecoveryStep(logger, id, item.Occurrence, RecoveryStates.Succeeded);
                return made;
            }

            if (handled.Trigger != ServiceRunTrigger.CatchUp)
            {
                // the scheduler ran it at its time (it failed or was skipped, with its own notice and retries): the pass does not run it again behind it; what follows it is blocked
                return item with { State = RecoveryStates.Failed, Reason = "its scheduled run did not succeed; see its history" };
            }
        }

        var before = item;
        item = item with { State = RecoveryStates.Running, Reason = null, Attempts = item.Attempts + 1, RunId = null };
        await ledger.SaveAsync(item, cancellationToken);
        RecoveryItemRow end;
        try
        {
            // dependents are planned steps of their own: no run-after chain starts them behind the plan
            var start = await runner.StartAsync(id, ServiceRunTrigger.CatchUp, Requester, cancellationToken, startDependents: false);
            if (start.Run is not { } run || start.AlreadyRunning)
            {
                end = item with { State = RecoveryStates.Failed, Reason = "the service could not be started" };
            }
            else
            {
                await runner.WhenServiceIdleAsync(id, cancellationToken);
                var last = (await runs.ListAsync(id, 0, 10, cancellationToken)).FirstOrDefault(r => r.StartedAt >= run.StartedAt && r.Trigger == ServiceRunTrigger.CatchUp) ?? run;
                end = last.Status switch
                {
                    ServiceRunStatus.Succeeded => item with { State = RecoveryStates.Succeeded, Reason = null, RunId = last.Id },

                    // the service could not do it now (no connected account, source down): not a failure and no attempt used; it is tried again at the next trigger and its dependents wait
                    ServiceRunStatus.Skipped => before with { State = RecoveryStates.Waiting, Reason = Clip("waiting: " + (last.Message ?? "the service had nothing to run on")), RunId = last.Id },
                    _ => item with { State = RecoveryStates.Failed, Reason = Clip(last.Message ?? "the run did not succeed"), RunId = last.Id },
                };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // one step that throws must not end the pass or leave its item running
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.RecoveryCrashed(logger, id, ex);
            end = item with { State = RecoveryStates.Failed, Reason = "the step failed unexpectedly" };
        }

        await ledger.SaveAsync(end, CancellationToken.None);
        ServicesLog.RecoveryStep(logger, id, item.Occurrence, end.State);
        return end;
    }

    private static string Clip(string text) => text.Length <= 400 ? text : text[..399] + "~";

    /// <summary>When the service must not run in the market hours and it is inside them on a trading day, the instant they are over; otherwise null.</summary>
    private DateTimeOffset? InMarketHours(RecoveryInput input, TradingCalendar calendar)
    {
        if (input.Definition.Constraints.MustNotOverlap is not { } quiet)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var ist = IndiaTime.ToIst(now);
        var today = DateOnly.FromDateTime(ist.DateTime);
        if (!calendar.IsTradingDay(today) || !quiet.Contains(TimeOnly.FromDateTime(ist.DateTime)))
        {
            return null;
        }

        return new DateTimeOffset(today.ToDateTime(quiet.To), IndiaTime.Offset).AddMinutes(1);
    }
}
