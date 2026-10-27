using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>
/// Spec P9-08 end to end on the real API and database with the engine's facts and the basket command faked: every route needs a session, accept needs the capability and the owner's fresh password, a second accept repeats the first result and changes nothing, reject records the decision
/// and closes the proposal, and the actor is the session. No model is involved.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class AssistantProposalApiTests(ApiFactory factory)
{
    private const string Owner = "owner:" + ApiFactory.OwnerEmail;

    private static readonly Guid BasketId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid EvbId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly (HttpMethod Method, string Path)[] Routes =
    [
        (HttpMethod.Get, "/api/assistant/proposals"), (HttpMethod.Get, "/api/assistant/proposals/1"), (HttpMethod.Post, "/api/assistant/proposals/1/accept"), (HttpMethod.Post, "/api/assistant/proposals/1/reject"),
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private (WebApplicationFactory<Program> Host, FakeAdd Add) NewHost()
    {
        var add = new FakeAdd();
        var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IProposalFacts>().AddScoped<IProposalFacts, Facts>();
            services.RemoveAll<ICommandHandler<AddBasketMembersCommand, AddMembersResult>>().AddSingleton<ICommandHandler<AddBasketMembersCommand, AddMembersResult>>(add);
        }));
        return (host, add);
    }

    private async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> host)
    {
        using var login = await factory.CreateAnonymousClient().LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);
        var session = (await login.Content.ReadFromJsonAsync<XDrishti.Application.Auth.SessionInfo>(Ct))!;
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"198.19.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}");
        client.DefaultRequestHeaders.Add("Cookie", $"xd.sid={ApiFactory.SessionCookieValue(login)}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", session.CsrfToken);
        return client;
    }

    private Task<int> ExecuteAsync(string sql) => factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(sql, Ct));

    private Task<int> SettingsAsync(bool allowAccept) =>
        ExecuteAsync($"DELETE FROM ai.settings; INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason, allow_proposal_accept) VALUES (true, false, 6, '{Owner}', 'test setup', {(allowAccept ? "true" : "false")})");

#pragma warning disable EF1002 // the values are constants and generated hex of these tests
    private async Task<long> SeedAsync()
    {
        var key = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        return await factory.WithDbAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText = $$"""
            INSERT INTO workflow.assistant_proposals (kind, dedupe_key, created_on, subject, payload, reason, evidence_json, checks_json, pack_hash, prompt_version, created_by, expires_at)
            VALUES ('basket_add', '{{key}}', CURRENT_DATE, 'Add EVB to basket EvalAlpha', '{"basketId":"{{BasketId}}","symbol":"EVB"}', 'EVB has 40 trades and a lower bound of 0.31.',
                    '{"basis":"gross R","population":[],"dateRange":null,"n":40,"figures":[],"limits":[]}', '[]', '{{new string('p', 64)}}', 'proposal.v1', 'system:proposals', now() + interval '3 days')
            RETURNING id
            """;
            return (long)(await command.ExecuteScalarAsync(Ct))!;
        });
    }

    private Task<long> CountAsync(string where) =>
        factory.WithDbAsync(async db => await db.Database.SqlQueryRaw<long>($"SELECT count(*) AS \"Value\" FROM workflow.assistant_proposal_decisions WHERE {where}").SingleAsync(Ct));

#pragma warning restore EF1002

    [Fact]
    public async Task Every_route_needs_a_session()
    {
        var anonymous = factory.CreateAnonymousClient();
        foreach (var (method, path) in Routes)
        {
            using var response = await anonymous.SendAsync(new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = method == HttpMethod.Get ? null : JsonContent.Create(new { }) }, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
        }
    }

    [Fact]
    public async Task The_inbox_is_read_while_everything_is_off_and_a_bad_query_or_id_is_refused()
    {
        await SettingsAsync(allowAccept: false);
        var (host, _) = NewHost();
        using var _ = host;
        var client = await SignedInAsync(host);
        var id = await SeedAsync();

        var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/assistant/proposals?state=pending&kind=basket_add&limit=100", UriKind.Relative), Ct);
        var one = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/assistant/proposals/{id}", UriKind.Relative), Ct);
        using var missing = await client.GetAsync(new Uri("/api/assistant/proposals/999999999", UriKind.Relative), Ct);
        using var badState = await client.GetAsync(new Uri("/api/assistant/proposals?state=bogus", UriKind.Relative), Ct);
        using var badLimit = await client.GetAsync(new Uri("/api/assistant/proposals?limit=101", UriKind.Relative), Ct);

        list.EnumerateArray().Any(p => p.GetProperty("id").GetInt64() == id).ShouldBeTrue();
        one.GetProperty("state").GetString().ShouldBe("pending");
        one.GetProperty("effect").GetString().ShouldBe("Adds EVB to basket EvalAlpha.");
        one.GetProperty("payload").GetProperty("symbol").GetString().ShouldBe("EVB");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        badState.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        badLimit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Accept_is_refused_while_the_capability_is_off_and_before_the_password_is_asked()
    {
        await SettingsAsync(allowAccept: false);
        var (host, add) = NewHost();
        using var _ = host;
        var client = await SignedInAsync(host);
        var id = await SeedAsync();

        using var off = await client.PostAsJsonAsync(new Uri($"/api/assistant/proposals/{id}/accept", UriKind.Relative), new { password = "wrong-on-purpose" }, Ct);

        off.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await off.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("assistant.capability_off");
        add.Calls.ShouldBeEmpty();
        (await CountAsync($"proposal_id = {id}")).ShouldBe(0);
    }

    [Fact]
    public async Task Accept_needs_a_right_fresh_password_applies_once_and_a_second_accept_repeats_the_first_result()
    {
        await SettingsAsync(allowAccept: true);
        var (host, add) = NewHost();
        using var _ = host;
        var client = await SignedInAsync(host);
        var id = await SeedAsync();
        var uri = new Uri($"/api/assistant/proposals/{id}/accept", UriKind.Relative);

        using var none = await client.PostAsJsonAsync(uri, new { password = (string?)null }, Ct);
        using var wrong = await client.PostAsJsonAsync(uri, new { password = "wrong-on-purpose" }, Ct);
        add.Calls.ShouldBeEmpty();
        using var first = await client.PostAsJsonAsync(uri, new { password = factory.OwnerPassword }, Ct);
        using var second = await client.PostAsJsonAsync(uri, new { password = factory.OwnerPassword }, Ct);

        none.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        wrong.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("state").GetString().ShouldBe("accepted");
        body.GetProperty("decision").GetProperty("actor").GetString().ShouldBe(Owner);
        body.GetProperty("decision").GetProperty("auto").GetBoolean().ShouldBeFalse();
        body.GetProperty("decision").GetProperty("resultRef").GetString().ShouldBe($"basket:{BasketId}");
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        add.Calls.Count.ShouldBe(1);
        (await CountAsync($"proposal_id = {id}")).ShouldBe(1);
        var command = add.Calls.Single();
        (command.BasketId, command.Actor).ShouldBe((BasketId, Owner));
    }

    [Fact]
    public async Task Reject_needs_no_password_records_the_decision_and_closes_the_proposal()
    {
        await SettingsAsync(allowAccept: true);
        var (host, add) = NewHost();
        using var _ = host;
        var client = await SignedInAsync(host);
        var id = await SeedAsync();

        using var reject = await client.PostAsJsonAsync(new Uri($"/api/assistant/proposals/{id}/reject", UriKind.Relative), new { reason = "not now" }, Ct);
        using var again = await client.PostAsJsonAsync(new Uri($"/api/assistant/proposals/{id}/reject", UriKind.Relative), new { reason = "again" }, Ct);
        using var accept = await client.PostAsJsonAsync(new Uri($"/api/assistant/proposals/{id}/accept", UriKind.Relative), new { password = factory.OwnerPassword }, Ct);
        using var tooLong = await client.PostAsJsonAsync(new Uri($"/api/assistant/proposals/{await SeedAsync()}/reject", UriKind.Relative), new { reason = new string('x', 301) }, Ct);

        reject.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reject.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("decision").GetProperty("reason").GetString().ShouldBe("not now");
        (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("decision").GetProperty("reason").GetString().ShouldBe("not now");
        accept.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await accept.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("assistant.proposal_closed");
        tooLong.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        add.Calls.ShouldBeEmpty();
    }

    private sealed class FakeAdd : ICommandHandler<AddBasketMembersCommand, AddMembersResult>
    {
        public List<AddBasketMembersCommand> Calls { get; } = [];

        public ValueTask<Result<AddMembersResult>> HandleAsync(AddBasketMembersCommand command, CancellationToken cancellationToken)
        {
            Calls.Add(command);
            return ValueTask.FromResult(Result.Success(new AddMembersResult(1, 1, 0, 0, 2)));
        }
    }

    /// <summary>The engine's facts for one tracked instrument in one manual basket; everything else is absent.</summary>
    private sealed class Facts : IProposalFacts
    {
        public ValueTask<IReadOnlyList<CellFact>> CellsAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<CellFact>>([]);

        public ValueTask<CellFact?> CellAsync(string cellKey, CancellationToken cancellationToken) => ValueTask.FromResult<CellFact?>(null);

        public ValueTask<RunFact?> RunAsync(string runKey, CancellationToken cancellationToken) => ValueTask.FromResult<RunFact?>(null);

        public ValueTask<IReadOnlyList<OpenHypothesisFact>> OpenHypothesesAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<OpenHypothesisFact>>([]);

        public ValueTask<IReadOnlyList<BasketFact>> BasketsAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<BasketFact>>([]);

        public ValueTask<BasketFact?> BasketAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult<BasketFact?>(id == BasketId ? new BasketFact(BasketId, "EvalAlpha", false, false, ["TCS"]) : null);

        public ValueTask<InstrumentFact?> InstrumentAsync(string symbol, CancellationToken cancellationToken) => ValueTask.FromResult<InstrumentFact?>(symbol == "EVB" ? new InstrumentFact(EvbId, "EVB", true, false) : null);

        public ValueTask<InstrumentTradesFact> InstrumentTradesAsync(string symbol, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new InstrumentTradesFact(symbol, "value", 40, 0.31, null, null, [], "gross R", []));

        public ValueTask<IReadOnlyList<InstrumentTradesFact>?> AllInstrumentTradesAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<InstrumentTradesFact>?>(null);
    }
}
