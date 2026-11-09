using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>
/// CF-12 leftover (ADR 0234) over HTTP and a real database: the owner dismisses a kill proposal with a fresh password and a reason; the actor is the session owner; no cell changes; a second dismiss changes nothing; a dismissed proposal cannot be applied; the record's own
/// checks (reason with state, owner actor) hold in the database.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class KillDismissApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private readonly string _strategy = "KD" + Guid.NewGuid().ToString("N")[..10];

    private string Cell(string side) => $"{_strategy}|15m|{side}|intraday|FIXED_RR|*";

    private static Uri Dismiss(long id) => new($"/api/confidence/kill-proposals/{id}/dismiss", UriKind.Relative);

    private static Uri Apply(long id) => new($"/api/confidence/kill-proposals/{id}/apply", UriKind.Relative);

    private async Task<long> ProposeAsync(params string[] cells)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IKillProposalStore>();
        var proposal = new NewKillProposal(_strategy, null, "cost_stress", false, -0.56, 0, 100, "the numbers", cells, new string('a', 64), 130);
        return (await store.InsertAsync(proposal, new DateOnly(2026, 11, 6), Ct))!.Value;
    }

    private async Task WalkAsync(string cell, params CellState[] states)
    {
        using var scope = Factory.Services.CreateScope();
        var record = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCellTransitionCommand, ChangelogOutcome>>();
        foreach (var state in states)
        {
            (await record.HandleAsync(new RecordCellTransitionCommand(cell, state, "setup", "test", new TransitionFacts(true, true, true, true, 20), ["run:setup"]), Ct)).IsSuccess.ShouldBeTrue();
        }
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, Uri uri, object? body = null, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body) };
        using var response = await (client ?? Client).SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> DismissAsync(long id, string? password, string? reason = "seen, a data glitch") =>
        SendAsync(HttpMethod.Post, Dismiss(id), new { reason, password, actor = "owner:evil@example.test" });

    private Task<string> StateAsync(long id) => ScalarAsync<string>("SELECT state FROM confidence.kill_proposals WHERE id = @i", ("i", id));

    [Fact]
    public async Task Without_a_session_it_is_401_and_without_the_CSRF_token_403()
    {
        var id = await ProposeAsync(Cell("BUY"));
        var anonymous = Factory.CreateAnonymousClient();
        var noCsrf = Factory.CreateAnonymousClient();
        noCsrf.DefaultRequestHeaders.Add("Cookie", Owner.CookieHeader);

        var unauthorized = await SendAsync(HttpMethod.Post, Dismiss(id), new { reason = "x", password = Factory.OwnerPassword }, anonymous);
        var forbidden = await SendAsync(HttpMethod.Post, Dismiss(id), new { reason = "x", password = Factory.OwnerPassword }, noCsrf);

        unauthorized.Status.ShouldBe(HttpStatusCode.Unauthorized);
        forbidden.Status.ShouldBe(HttpStatusCode.Forbidden);
        forbidden.Body.GetProperty("code").GetString().ShouldBe("auth.csrf_invalid");
        (await StateAsync(id)).ShouldBe("proposed");
    }

    [Fact]
    public async Task A_missing_password_is_403_step_up_and_a_wrong_one_401_and_nothing_is_dismissed()
    {
        var id = await ProposeAsync(Cell("BUY"));

        var missing = await DismissAsync(id, null);
        var wrong = await DismissAsync(id, "not-the-password");

        missing.Status.ShouldBe(HttpStatusCode.Forbidden);
        missing.Body.GetProperty("code").GetString().ShouldBe("auth.step_up_required");
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        wrong.Body.GetProperty("code").GetString().ShouldBe("auth.invalid_credentials");
        (await StateAsync(id)).ShouldBe("proposed");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_reason_is_400(string? reason)
    {
        var id = await ProposeAsync(Cell("BUY"));

        var result = await DismissAsync(id, Factory.OwnerPassword, reason);

        result.Status.ShouldBe(HttpStatusCode.BadRequest);
        result.Body.GetProperty("code").GetString().ShouldBe("confidence.reason_invalid");
        (await StateAsync(id)).ShouldBe("proposed");
    }

    [Fact]
    public async Task The_owner_dismisses_with_the_session_as_actor_changes_no_cell_and_a_second_dismiss_changes_nothing()
    {
        var cell = Cell("BUY");
        await WalkAsync(cell, CellState.Candidate, CellState.Paper);
        var id = await ProposeAsync(cell);

        var first = await DismissAsync(id, Factory.OwnerPassword, "first reason");
        var second = await DismissAsync(id, Factory.OwnerPassword, "second reason");

        first.Status.ShouldBe(HttpStatusCode.OK);
        first.Body.GetProperty("state").GetString().ShouldBe("dismissed");
        first.Body.GetProperty("alreadyDismissed").GetBoolean().ShouldBeFalse();
        first.Body.GetProperty("actor").GetString().ShouldStartWith("owner:");
        first.Body.GetProperty("actor").GetString()!.ShouldNotContain("evil");
        second.Status.ShouldBe(HttpStatusCode.OK);
        second.Body.GetProperty("alreadyDismissed").GetBoolean().ShouldBeTrue();
        second.Body.GetProperty("reason").GetString().ShouldBe("first reason");
        (await ScalarAsync<string>("SELECT dismiss_reason FROM confidence.kill_proposals WHERE id = @i", ("i", id))).ShouldBe("first reason");
        (await ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject = @s AND to_state = 'paused'", ("s", cell))).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM confidence.kill_settings")).ShouldBe(0);

        var list = await SendAsync(HttpMethod.Get, new Uri("/api/confidence/kill-proposals?state=dismissed", UriKind.Relative));
        list.Body.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt64() == id).GetProperty("dismissReason").GetString().ShouldBe("first reason");
    }

    [Fact]
    public async Task A_dismissed_proposal_cannot_be_applied_and_an_applied_one_cannot_be_dismissed()
    {
        var cell = Cell("BUY");
        await WalkAsync(cell, CellState.Candidate, CellState.Paper);
        var dismissed = await ProposeAsync(cell);
        (await DismissAsync(dismissed, Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);

        var apply = await SendAsync(HttpMethod.Post, Apply(dismissed));

        apply.Status.ShouldBe(HttpStatusCode.Conflict);
        apply.Body.GetProperty("code").GetString().ShouldBe("confidence.proposal_closed");

        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IKillProposalStore>();
        var other = (await store.InsertAsync(new NewKillProposal(_strategy, null, "drawdown", false, 1, 1, 100, "t", [cell], new string('a', 64), 130), new DateOnly(2026, 11, 6), Ct))!.Value;
        (await SendAsync(HttpMethod.Post, Apply(other))).Status.ShouldBe(HttpStatusCode.OK);

        var late = await DismissAsync(other, Factory.OwnerPassword);

        late.Status.ShouldBe(HttpStatusCode.Conflict);
        late.Body.GetProperty("code").GetString().ShouldBe("confidence.proposal_applied");
    }

    [Fact]
    public async Task An_unknown_proposal_is_404()
    {
        var result = await DismissAsync(long.MaxValue, Factory.OwnerPassword);

        result.Status.ShouldBe(HttpStatusCode.NotFound);
        result.Body.GetProperty("code").GetString().ShouldBe("confidence.proposal_not_found");
    }

    [Fact]
    public async Task The_database_ties_the_reason_to_the_state_and_refuses_an_automatic_dismissal()
    {
        var id = await ProposeAsync(Cell("BUY"));
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IKillProposalStore>();

        (await store.DecideAsync(id, "dismissed", "owner:me@example.test", null, Ct)).ShouldBe(KillDecideResult.Refused);
        (await store.DismissAsync(id, "owner:me@example.test", "   ", Ct)).ShouldBe(KillDecideResult.Refused);
        (await StateAsync(id)).ShouldBe("proposed");
        (await store.DismissAsync(id, "owner:me@example.test", "fine", Ct)).ShouldBe(KillDecideResult.Decided);
        (await store.DismissAsync(id, "owner:me@example.test", "again", Ct)).ShouldBe(KillDecideResult.NotProposed);
    }
}
