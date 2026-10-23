using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec P7-13 on a real database: a grade is stored once per ticket (the second insert is skipped), the plan's grade and the decisions report read it with the label, and the database refuses a second grade and a changed label.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class GradeApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Session = new(2099, 2, 5);

    private static NewPlanItem Item(int rank, TicketGroup group) =>
        new(
            rank, group, "NSE:RELIANCE", "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30",
            group == TicketGroup.Primary ? [] : ["rank_below_cut"],
            new PlanEvidence("ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*", 41, "replayed history", "cell statistics only", 0.52, 0.12, [], null), []);

    private static NewPlan Plan() =>
        new("plan-2099-02-05-grade", Session, new DateTimeOffset(2099, 2, 5, 3, 35, 0, TimeSpan.Zero), 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch,
            [Item(1, TicketGroup.Primary), Item(2, TicketGroup.Reserve), Item(3, TicketGroup.Rejected)]);

    private static TicketGrade Graded(double r) => new(GradeStatus.Graded, r, 0.4, 2.0, "Target", 100, 105, "gross, no charges", "entered, left");

    private async Task<(long Plan, IReadOnlyList<PlanItemRow> Items)> StoredAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPlanStore>();
        var published = await store.PublishAsync(Plan(), Ct);
        return (published.Id, await store.ItemsAsync(published.Id, Ct));
    }

    private async Task<int> GradeAsync(params NewGrade[] grades)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IGradeStore>().InsertAsync(grades, Ct);
    }

    [Fact]
    public async Task Grading_twice_stores_one_grade_per_ticket_and_the_first_one_stays()
    {
        var (plan, items) = await StoredAsync();
        var first = new NewGrade(plan, items[0].Id, Session, Graded(1.5), "run", DateTimeOffset.UnixEpoch);
        var again = first with { Grade = Graded(-3) };

        var one = await GradeAsync(first);
        var two = await GradeAsync(again);

        one.ShouldBe(1);
        two.ShouldBe(0);
        using var scope = Factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IGradeStore>().ForPlansAsync([plan], Ct);
        stored[items[0].Id].Grade.Grade.R.ShouldBe(1.5);
    }

    [Fact]
    public async Task The_plan_grade_and_the_decisions_report_are_read_with_the_label_and_a_missing_grade_is_pending()
    {
        var (plan, items) = await StoredAsync();
        await GradeAsync(
            new NewGrade(plan, items[0].Id, Session, Graded(2), null, DateTimeOffset.UnixEpoch),
            new NewGrade(plan, items[1].Id, Session, new TicketGrade(GradeStatus.NotTriggered, null, null, null, null, null, null, "gross, no charges", "entry condition did not hold"), null, DateTimeOffset.UnixEpoch));

        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/plans/{plan}/grade", UriKind.Relative), Ct);
        var report = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/reports/decisions?from={Session:yyyy-MM-dd}&to={Session:yyyy-MM-dd}", UriKind.Relative), Ct);

        view.GetProperty("basis").GetString().ShouldBe("simulated on plan prices");
        view.GetProperty("pending").GetInt32().ShouldBe(1);
        view.GetProperty("tickets").EnumerateArray().Select(t => t.GetProperty("status").GetString()).ShouldBe(["graded", "not_triggered", "pending"]);
        report.GetProperty("basis").GetString().ShouldBe("simulated on plan prices");
        report.GetProperty("curves").EnumerateArray().First().GetProperty("totals").GetProperty("totalR").GetDouble().ShouldBe(2);
        report.GetProperty("plans").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_plan_is_not_found_and_a_reversed_range_is_refused()
    {
        using var missing = await Client.GetAsync(new Uri("/api/plans/999999/grade", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri("/api/reports/decisions?from=2099-02-06&to=2099-02-05", UriKind.Relative), Ct);

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_database_refuses_a_grade_for_the_wrong_plan_a_changed_label_and_an_r_on_a_not_triggered_ticket()
    {
        var (plan, items) = await StoredAsync();
        var wrongPlan = new NewGrade(plan + 1_000, items[0].Id, Session, Graded(1), null, DateTimeOffset.UnixEpoch);
        var badShape = new NewGrade(plan, items[2].Id, Session, new TicketGrade(GradeStatus.NotTriggered, 1.0, null, null, null, null, null, "gross", "x"), null, DateTimeOffset.UnixEpoch);

        await Should.ThrowAsync<Npgsql.PostgresException>(() => GradeAsync(wrongPlan));
        await Should.ThrowAsync<Npgsql.PostgresException>(() => GradeAsync(badShape));
    }
}
