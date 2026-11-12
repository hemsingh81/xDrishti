using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Auth;
using XDrishti.Application.AutoSwitch;
using XDrishti.Application.Learning.CellsRefresh;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Planning;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Platform.Services.Svc;

namespace XDrishti.Application.Tests.Learning.CellsRefresh;

/// <summary>The service definition, the owner-act resume, the gate text and the bell notice of the scheduled refresh (spec LC-01, ACs 1, 2, 3, 6 and 8).</summary>
public sealed class CellsRefreshServiceTests
{
    private static readonly DateOnly Friday = new(2026, 10, 23);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServicesWorld World(ICellStatsStore? cells = null, bool nightlyOn = true, bool refreshOn = false) =>
        new(
            (_, _) =>
            [
                Make("nightly", Schedule.Daily(T(18, 30)), enabled: nightlyOn),
                Make(CellsRefreshService.Id, Schedule.Daily(T(16, 0)), enabled: refreshOn, constraints: new ServiceConstraints { ResumeIsOwnerAct = true }),
                Make("plain", Schedule.Daily(T(17, 0)), enabled: false),
            ],
            p =>
            {
                p.Singleton(cells ?? Substitute.For<ICellStatsStore>());
                p.Singleton(new CellsRefreshOptions());
                p.Scoped<CellsRefreshState>();
            });

    [Fact]
    public void The_service_is_paused_at_first_start_after_eod_latest_only_and_not_in_the_nightlys_must_follow()
    {
        var d = CellsRefreshService.Definition;

        d.Id.ShouldBe("cells-refresh");
        d.Defaults.Enabled.ShouldBeFalse();
        d.Defaults.Schedule.ShouldBe(Schedule.AfterService(EodService.Id));
        d.Constraints.RecoveryClass.ShouldBe(RecoveryClass.LatestOnly);
        d.Constraints.NotBeforeSessionEnd.ShouldBeTrue();
        d.Constraints.ResumeIsOwnerAct.ShouldBeTrue();
        NightlyService.Definition.Constraints.MustFollow.ShouldNotContain("cells-refresh");
        NightlyService.Definition.Constraints.ResumeIsOwnerAct.ShouldBeFalse();
    }

    [Fact]
    public void Auto_chain_has_six_services_and_includes_the_refresh()
    {
        AutoSwitchService.Chain.Count.ShouldBe(6);
        AutoSwitchService.Chain.ShouldContain("cells-refresh");
    }

    [Fact]
    public async Task Resuming_a_flagged_service_without_a_fresh_password_is_refused_and_it_stays_paused()
    {
        using var world = World();

        var result = await world.ResumeAsync(CellsRefreshService.Id);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrors.StepUpRequiredCode);
        (await world.ViewAsync(CellsRefreshService.Id)).Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Resuming_a_flagged_service_with_a_fresh_password_resumes_it_and_the_view_says_it_needs_one()
    {
        using var world = World();

        var result = await world.WithScopeAsync(sp => sp.GetRequiredService<XDrishti.Application.Abstractions.Messaging.ICommandHandler<XDrishti.Application.Platform.Services.ResumeServiceCommand, XDrishti.Application.Platform.Services.ServiceView>>()
            .HandleAsync(new XDrishti.Application.Platform.Services.ResumeServiceCommand(CellsRefreshService.Id, "owner:me@x.test", FreshPassword: true), Ct));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Enabled.ShouldBeTrue();
        result.Value.ResumeNeedsPassword.ShouldBeTrue();
    }

    [Fact]
    public async Task Every_other_service_resumes_with_a_session_as_before()
    {
        using var world = World();

        var result = await world.ResumeAsync("plain");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ResumeNeedsPassword.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    public void The_gate_passes_at_age_0_and_5(int daysBack, bool pass)
    {
        var asOf = daysBack == 0 ? Friday : Friday.AddDays(-7); // 5 sessions

        NightlyChecks.Selection(asOf, Context(), "paused").Status.ShouldBe(pass ? GateStatus.Pass : GateStatus.Fail);
    }

    [Fact]
    public void The_gate_fails_at_age_6_and_names_the_last_refresh_state_and_is_unavailable_with_no_snapshot()
    {
        var failed = NightlyChecks.Selection(Friday.AddDays(-8), Context(), "failed on 2026-10-22: cells_refresh.no_holdout");

        failed.Status.ShouldBe(GateStatus.Fail);
        failed.Reason.ShouldContain("6 sessions old (the limit is 5)");
        failed.Reason.ShouldContain("Last refresh: failed on 2026-10-22: cells_refresh.no_holdout.");
        NightlyChecks.Selection(null, Context(), "never ran").Status.ShouldBe(GateStatus.Unavailable);
        NightlyChecks.Selection(Friday.AddDays(-8), Context()).Reason.ShouldNotContain("Last refresh");
    }

    [Fact]
    public async Task The_state_says_paused_never_ran_or_failed_with_the_code()
    {
        using var paused = World(refreshOn: false);
        using var never = World(refreshOn: true);

        (await paused.WithScopeAsync(sp => sp.GetRequiredService<CellsRefreshState>().DescribeAsync(Ct))).ShouldBe("paused");
        (await never.WithScopeAsync(sp => sp.GetRequiredService<CellsRefreshState>().DescribeAsync(Ct))).ShouldBe("never ran");
        CellsRefreshState.CodeOf("cells_refresh.no_snapshot: No snapshot.").ShouldBe("cells_refresh.no_snapshot");
    }

    [Theory]
    [InlineData(6, 1)] // 2026-10-06: Thu, Fri and the days after: 3 sessions behind the last closed session of Monday 2026-10-12 11:00, which is Friday 10-09
    [InlineData(7, 0)] // 2 sessions behind
    public async Task The_bell_speaks_at_three_sessions_behind_and_not_at_two(int asOfDay, int expected)
    {
        var cells = Substitute.For<ICellStatsStore>();
        cells.NewestAsOfAsync(Arg.Any<CancellationToken>()).Returns(new DateOnly(2026, 10, asOfDay));
        using var world = World(cells);

        var notices = await world.WithScopeAsync(sp => sp.GetRequiredService<XDrishti.Application.Abstractions.Messaging.IQueryHandler<XDrishti.Application.Platform.Services.GetServiceNoticesQuery, XDrishti.Application.Platform.Services.ServiceNotices>>().HandleAsync(new XDrishti.Application.Platform.Services.GetServiceNoticesQuery(), Ct));

        var stale = notices.Items.Where(n => n.Kind == "cells_stale").ToList();
        stale.Count.ShouldBe(expected);
        if (expected == 1)
        {
            stale[0].Text.ShouldBe("Cell statistics are 3 sessions old; the nightly plan fails at 6. Last refresh: paused.");
            stale[0].Id.ShouldBe("cells:stale:2026-10-12");
        }
    }

    [Fact]
    public async Task With_the_nightly_plan_paused_there_is_no_notice()
    {
        var cells = Substitute.For<ICellStatsStore>();
        cells.NewestAsOfAsync(Arg.Any<CancellationToken>()).Returns(new DateOnly(2026, 9, 1));
        using var world = World(cells, nightlyOn: false);

        var notices = await world.WithScopeAsync(sp => sp.GetRequiredService<XDrishti.Application.Abstractions.Messaging.IQueryHandler<XDrishti.Application.Platform.Services.GetServiceNoticesQuery, XDrishti.Application.Platform.Services.ServiceNotices>>().HandleAsync(new XDrishti.Application.Platform.Services.GetServiceNoticesQuery(), Ct));

        notices.Items.ShouldNotContain(n => n.Kind == "cells_stale");
    }

    private static GateContext Context() => new(Friday, new DateOnly(2026, 10, 26), new XDrishti.Domain.Market.Calendar.TradingCalendar(), new DateTimeOffset(2026, 10, 23, 13, 0, 0, TimeSpan.Zero));
}
