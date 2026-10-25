using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec P8-13 on a real database: the smart-rule and loss-tag reads answer on empty data with named non-values and refuse bad input.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LearningEvidenceApiTests(ApiFactory factory) : DataTestBase(factory)
{
    [Fact]
    public async Task Smart_rules_without_a_plan_has_five_empty_rules_and_not_evaluable_outcomes()
    {
        using var response = await Client.GetAsync(new Uri("/api/learning/smart-rules?plans=30", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        view.GetProperty("state").GetString().ShouldBe("empty");
        view.GetProperty("rules").GetArrayLength().ShouldBe(5);
        foreach (var rule in view.GetProperty("rules").EnumerateArray())
        {
            rule.GetProperty("unavailableOn").ValueKind.ShouldBe(JsonValueKind.Null);
            rule.GetProperty("outcome").GetProperty("state").GetString().ShouldBe("not_evaluable");
            rule.GetProperty("outcome").GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    [Theory]
    [InlineData("/api/learning/smart-rules?plans=0")]
    [InlineData("/api/learning/smart-rules?plans=61")]
    [InlineData("/api/learning/loss-tags?run=nokey")]
    [InlineData("/api/learning/loss-tags?run=zz:swing:X")]
    public async Task Bad_input_is_refused_with_422_learning_invalid(string url)
    {
        using var response = await Client.GetAsync(new Uri(url, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("learning.invalid");
    }

    [Fact]
    public async Task Loss_tags_answer_and_list_what_is_not_built_and_an_unknown_run_is_404()
    {
        var run = new string('a', 64);
        using var unknown = await Client.GetAsync(new Uri($"/api/learning/loss-tags?run={run}:swing:X", UriKind.Relative), Ct);
        using var response = await Client.GetAsync(new Uri("/api/learning/loss-tags", UriKind.Relative), Ct);

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        view.GetProperty("state").GetString().ShouldBeOneOf("empty", "ok");
        view.GetProperty("notBuilt").GetArrayLength().ShouldBeGreaterThan(0);
    }

    private async Task<string> StoreRunAsync(string strategy = "ORB")
    {
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo(), strategy: strategy);
        using var scope = Factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>().HandleAsync(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor), Ct);
        stored.IsSuccess.ShouldBeTrue();
        return run.RunKey;
    }

    [Fact]
    public async Task A_seeded_run_gives_tagged_cells_with_R_lost_and_two_runs_of_one_strategy_are_409()
    {
        var key = await StoreRunAsync();
        var second = await StoreRunAsync();

        // The exit label is the rest of the text after the second colon, so it may hold ':'.
        using var response = await Client.GetAsync(new Uri($"/api/learning/loss-tags?run={key}:intraday:FIXED_RR:x", UriKind.Relative), Ct);
        using var twice = await Client.GetAsync(new Uri($"/api/learning/loss-tags?run={key}:intraday:A&run={second}:intraday:B", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        view.GetProperty("state").GetString().ShouldBe("ok");
        view.GetProperty("losses").GetInt32().ShouldBeGreaterThan(0);
        var cells = view.GetProperty("cells").EnumerateArray().ToList();
        cells.Count.ShouldBeGreaterThan(0);
        cells.ShouldAllBe(c => c.GetProperty("cellKey").GetString()!.Contains("|intraday|FIXED_RR:x|", StringComparison.Ordinal));
        cells.SelectMany(c => c.GetProperty("tags").EnumerateArray()).ShouldContain(t => t.GetProperty("count").GetInt32() > 0 && t.GetProperty("lostR").GetDouble() < 0);
        twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await twice.Content.ReadAsStringAsync(Ct)).ShouldContain("learning.cell_duplicate");
    }

    [Fact]
    public async Task Seeded_plans_with_codes_are_counted_and_the_flagged_plan_is_listed()
    {
        var evidence = new PlanEvidence("ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*", 41, "replayed history", "cell statistics only", 0.52, 0.12, ["gate_unavailable", "rule_removed_half"], null);
        NewPlanItem Item(int rank, TicketGroup group, string[] reasons) =>
            new(rank, group, "NSE:RELIANCE", "BUY", "ORB", new string('a', 64), "t", "s", "g", "v", reasons, evidence, []);
        var date = new DateOnly(2099, 12, 30);
        var plan = new NewPlan("plan-2099-12-30-smart-rules", date, new DateTimeOffset(2099, 12, 30, 3, 35, 0, TimeSpan.Zero), 1, new string('d', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch,
            [Item(1, TicketGroup.Primary, []), Item(2, TicketGroup.Reserve, ["gate_no_trade"]), Item(3, TicketGroup.Reserve, ["same_bet_better_ticket"])]);
        using var scope = Factory.Services.CreateScope();
        var published = await scope.ServiceProvider.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct);

        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/learning/smart-rules?plans=60", UriKind.Relative), Ct);

        view.GetProperty("state").GetString().ShouldBe("observed");
        var rules = view.GetProperty("rules").EnumerateArray().ToDictionary(r => r.GetProperty("id").GetString()!);
        rules["gate"].GetProperty("unavailableOn").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        rules["gate"].GetProperty("moved").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        rules["correlation"].GetProperty("moved").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        rules["meta"].GetProperty("state").GetString().ShouldBe("not_recorded");
        rules["gate"].GetProperty("ranOn").ValueKind.ShouldBe(JsonValueKind.Null);
        view.GetProperty("flaggedPlans").EnumerateArray().ShouldContain(f => f.GetProperty("planId").GetInt64() == published.Id);
    }
}
