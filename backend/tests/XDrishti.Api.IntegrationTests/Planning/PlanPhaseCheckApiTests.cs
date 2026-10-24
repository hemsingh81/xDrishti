using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec P7-18 on a real database: the phase check endpoint reads the stored plan runs, never prints MET on thin history, meets the exit on ten consecutive scheduled sessions, is broken by a manual run, and writes nothing.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class PlanPhaseCheckApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateTimeOffset At = new(2026, 10, 9, 13, 0, 0, TimeSpan.Zero);

    private async Task<long> PublishAsync(DateOnly forDate, bool manual = false)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPlanStore>();
        var plan = new NewPlan("nightly:" + forDate.ToString("yyyy-MM-dd"), forDate, new DateTimeOffset(forDate.ToDateTime(new TimeOnly(9, 5)), TimeSpan.FromMinutes(330)).ToUniversalTime(), 1, new string('a', 64), "skeleton: no builder", true, "system:nightly", At, []);
        return (await store.PublishWithRunAsync(plan, new PublishRun(manual, At, At.AddMinutes(1), "Published"), Ct)).Id;
    }

    private async Task PublishWeekdaysAsync(int manualIndex = -1)
    {
        var index = 0;
        for (var date = new DateOnly(2026, 9, 14); date <= new DateOnly(2026, 10, 12); date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                await PublishAsync(date, index++ == manualIndex);
            }
        }
    }

    private async Task<JsonElement> ReadAsync(string query = "")
    {
        using var response = await Client.GetAsync(new Uri("/api/plan-runs/phase-check" + query, UriKind.Relative), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task An_empty_database_is_not_evaluable_with_the_limits_and_nothing_is_written()
    {
        var view = await ReadAsync();

        view.GetProperty("report").GetProperty("exitVerdict").GetString().ShouldBe("NOT EVALUABLE");
        view.GetProperty("report").GetProperty("sessions").GetArrayLength().ShouldBe(10);
        view.GetProperty("report").GetProperty("criteria").GetArrayLength().ShouldBe(12);
        view.GetProperty("report").GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(5);
        view.GetProperty("text").GetString()!.ShouldContain("limit: about 2.1 years of data");
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_runs")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plans")).ShouldBe(0);
    }

    [Fact]
    public async Task Ten_consecutive_scheduled_sessions_meet_the_exit_and_reading_it_changes_nothing()
    {
        await PublishWeekdaysAsync();
        var before = await ScalarAsync<long>("SELECT count(*) FROM trading.plan_runs");

        var view = await ReadAsync();

        view.GetProperty("report").GetProperty("exitVerdict").GetString().ShouldBe("MET");
        view.GetProperty("report").GetProperty("exitCriterion").GetString().ShouldBe("MET (COUNT ONLY: all 10 plans are empty, no ticket was ever proposed)"); // the stored plans are the empty skeletons
        view.GetProperty("text").GetString()!.ShouldContain("exit criterion (1): MET (COUNT ONLY");
        view.GetProperty("report").GetProperty("streak").GetInt32().ShouldBe(10);
        view.GetProperty("report").GetProperty("through").GetString().ShouldBe("2026-10-12");
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_runs")).ShouldBe(before);
    }

    [Fact]
    public async Task A_manual_run_in_the_window_breaks_it()
    {
        await PublishWeekdaysAsync(manualIndex: 18);

        var view = await ReadAsync();

        view.GetProperty("report").GetProperty("exitVerdict").GetString().ShouldBe("NOT MET");
        view.GetProperty("report").GetProperty("sessions").EnumerateArray().Count(s => s.GetProperty("state").GetString() == "manual_run").ShouldBe(1);
    }

    [Fact]
    public async Task Four_sessions_of_history_are_not_evaluable()
    {
        foreach (var date in new[] { new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 12) })
        {
            await PublishAsync(date);
        }

        var view = await ReadAsync();

        view.GetProperty("report").GetProperty("exitVerdict").GetString().ShouldBe("NOT EVALUABLE");
        view.GetProperty("report").GetProperty("streak").GetInt32().ShouldBe(4);
    }

    [Fact]
    public async Task The_phase_check_needs_a_signed_in_owner()
    {
        using var anonymous = Factory.CreateClient();

        using var response = await anonymous.GetAsync(new Uri("/api/plan-runs/phase-check", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_date_after_the_newest_planned_session_is_a_422()
    {
        using var response = await Client.GetAsync(new Uri("/api/plan-runs/phase-check?through=2026-10-20", UriKind.Relative), Ct);

        ((int)response.StatusCode).ShouldBe(422);
    }
}
