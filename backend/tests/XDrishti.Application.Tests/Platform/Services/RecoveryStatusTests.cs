using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Platform.Services.ServicesWorld;
using static XDrishti.Application.Tests.Platform.Services.Svc;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>The Recovery screen's read model and its retry (spec AU-04): statuses per session and service, counts, bounds, the empty state, the audited retry and its 409 while a pass runs. Monday 2026-10-12, 10:00 India time.</summary>
public sealed class RecoveryStatusTests : IDisposable
{
    private readonly TestService _eod = Eod();
    private readonly TestService _other = Make("other", Schedule.Daily(T(16, 0)));
    private readonly ServicesWorld _world;

    public RecoveryStatusTests()
    {
        _world = new ServicesWorld(_eod, _other);
        foreach (var service in _world.Services)
        {
            _world.Data.Store(service.Definition.Id, service.Definition.Defaults);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _world.Dispose();

    private Task<Result<RecoveryStatusView>> StatusAsync(int days = 14) =>
        _world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<GetRecoveryStatusQuery, Result<RecoveryStatusView>>>().HandleAsync(new GetRecoveryStatusQuery(days), Ct));

    private Task<Result<RecoveryRetryAccepted>> RetryAsync() =>
        _world.WithScopeAsync(sp => sp.GetRequiredService<ICommandHandler<RetryRecoveryCommand, RecoveryRetryAccepted>>().HandleAsync(new RetryRecoveryCommand("owner@example.invalid"), Ct));

    private static RecoveryItemRow Item(Guid pass, string service, DateOnly day, string state, string? reason = null, int attempts = 1, string? detail = null) =>
        new(Guid.NewGuid(), pass, service, day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), day, day, state, reason, attempts, null, detail);

    [Fact]
    public async Task Days_outside_1_to_90_are_refused()
    {
        foreach (var days in new[] { 0, 91, -1 })
        {
            var result = await StatusAsync(days);
            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe(ServiceErrors.InvalidCode);
        }
    }

    [Fact]
    public async Task With_nothing_missed_and_an_empty_ledger_there_are_no_sessions_and_no_pass()
    {
        // both services ran just now: nothing is missed
        _world.Data.Store("eod", _eod.Definition.Defaults);
        await _world.Runner.StartAsync("eod", ServiceRunTrigger.Manual, "owner", Ct);
        await _world.Runner.StartAsync("other", ServiceRunTrigger.Manual, "owner", Ct);
        await _world.SettleAsync();

        var view = (await StatusAsync()).Value;

        view.Sessions.ShouldBeEmpty();
        view.LatestPass.ShouldBeNull();
        view.Counts.ShouldBe(new RecoveryCounts(0, 0, 0));
        view.Running.ShouldBeFalse();
        view.CheckedAt.ShouldBe(_world.Clock.GetUtcNow());
    }

    [Fact]
    public async Task Missed_sessions_that_no_pass_has_covered_are_listed_as_missed_with_the_oldest_session_first()
    {
        var view = (await StatusAsync()).Value;

        view.Counts.Missed.ShouldBeGreaterThan(0);
        view.Sessions.Select(s => s.Session).ShouldBe(view.Sessions.Select(s => s.Session).Order());
        view.Sessions.SelectMany(s => s.Items).ShouldContain(i => i.Status == RecoveryRowStatus.Missed && i.CanRetry);
    }

    [Fact]
    public async Task The_ledger_states_map_to_the_status_words_and_the_counts_follow()
    {
        var day = new DateOnly(2026, 10, 12);
        var pass = await _world.Ledger.BeginPassAsync("startup", At(12, 9, 0), Ct);
        await _world.Ledger.SaveAsync(Item(pass, "eod", day, RecoveryStates.Succeeded), Ct);
        await _world.Ledger.SaveAsync(Item(pass, "other", day, RecoveryStates.Failed, "source down", 3), Ct);
        await _world.Ledger.SaveAsync(Item(pass, "eod", day.AddDays(-1), RecoveryStates.Partial, "bars missing", detail: """{"qualityVerified":true,"runStartedAt":"2026-10-12T00:00:00+00:00","sessions":[{"date":"2026-10-09","state":"still_missing","instruments":3,"complete":2,"unfillable":0,"missing":1}]}"""), Ct);
        await _world.Ledger.SaveAsync(Item(pass, "other", day.AddDays(-1), RecoveryStates.Superseded), Ct);
        await _world.Ledger.EndPassAsync(pass, "failed", At(12, 9, 5), Ct);

        var view = (await StatusAsync()).Value;

        var rows = view.Sessions.SelectMany(s => s.Items).ToList();
        rows.Single(r => r.ServiceId == "eod" && r.To == day).Status.ShouldBe(RecoveryRowStatus.Recovered);
        var failed = rows.Single(r => r.ServiceId == "other" && r.To == day);
        (failed.Status, failed.Reason, failed.Attempts, failed.CanRetry).ShouldBe((RecoveryRowStatus.StillFailing, "source down", 3, true));
        var partial = rows.Single(r => r.Status == RecoveryRowStatus.Partial);
        partial.MissingSessions.ShouldBe([new DateOnly(2026, 10, 9)]);
        rows.ShouldContain(r => r.Status == RecoveryRowStatus.Superseded && !r.CanRetry);
        view.Counts.Recovered.ShouldBe(1);
        view.Counts.StillFailing.ShouldBe(2);
        (view.LatestPass!.Trigger, view.LatestPass.Status).ShouldBe(("startup", "failed"));
    }

    [Fact]
    public async Task A_large_ledger_is_bounded_and_says_so()
    {
        var pass = await _world.Ledger.BeginPassAsync("startup", At(12, 9, 0), Ct);
        for (var i = 0; i < 700; i++)
        {
            var day = new DateOnly(2026, 10, 12).AddDays(-(i % 80));
            await _world.Ledger.SaveAsync(Item(pass, $"svc{i}", day, RecoveryStates.Succeeded), Ct);
        }

        var view = (await StatusAsync(90)).Value;

        view.Truncated.ShouldBeTrue();
        view.Sessions.Sum(s => s.Items.Count).ShouldBeGreaterThanOrEqualTo(500);
        view.Counts.Recovered.ShouldBe(500);
    }

    [Fact]
    public async Task Old_ledger_rows_outside_the_days_window_are_not_shown()
    {
        var pass = await _world.Ledger.BeginPassAsync("startup", At(12, 9, 0), Ct);
        await _world.Ledger.SaveAsync(Item(pass, "eod", new DateOnly(2026, 8, 3), RecoveryStates.Failed, "old"), Ct);

        var view = (await StatusAsync(7)).Value;

        view.Sessions.SelectMany(s => s.Items).ShouldNotContain(r => r.Reason == "old");
    }

    [Fact]
    public async Task Retry_is_audited_with_the_actor_and_starts_a_pass_even_inside_the_debounce()
    {
        _world.Application.StartedAt = At(12, 9, 0);
        _world.Recovery.Request(RecoveryTrigger.Wake);
        await _world.SettleAsync();

        var result = await RetryAsync();
        await _world.SettleAsync();

        result.IsSuccess.ShouldBeTrue();
        _world.Data.Events.ShouldContain(e => e.Actor == "owner@example.invalid");
    }

    [Fact]
    public async Task Retry_while_a_pass_runs_is_refused_with_recovery_already_running_and_writes_no_audit()
    {
        var gate = _eod.Gate();
        _world.Recovery.Request(RecoveryTrigger.Wake);
        await _eod.WhenStartedAsync();
        var events = _world.Data.Events.Count;

        var result = await RetryAsync();
        var view = (await StatusAsync()).Value;
        gate.SetResult(ServiceOutcome.Succeeded());
        await _world.SettleAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecoveryErrors.AlreadyRunningCode);
        _world.Data.Events.Count.ShouldBe(events);
        view.Running.ShouldBeTrue();
    }
}
