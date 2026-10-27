using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Infrastructure.Ai.Assistant;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>Spec P9-08 AC 11 and the migrations on a throw-away server with the runtime role as in production: the inbox tables are insert-only, a proposal has one decision, one row per key and day, the rules of the tables hold, and the hypothesis log accepts source model and still refuses another.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AssistantProposalDatabaseTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private const string Owner = "owner:o@example.invalid";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NewProposal Proposal(string key, string kind = "basket_add", string reason = "r") =>
        new(kind, key.PadRight(64, 'k'), new DateOnly(2026, 10, 28), "Add EVB to basket B", """{"basketId":"11111111-1111-1111-1111-111111111111","symbol":"EVB"}""", reason, """{"basis":"gross","population":[],"dateRange":null,"n":1,"figures":[],"limits":[]}""", "[]", new string('p', 64), "m.gguf", null, "proposal.v1", "system:proposals", DateTimeOffset.UtcNow.AddDays(3));

    private async Task<bool> HasAsync(string table, string privilege)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', @t, @p)", connection);
        check.Parameters.AddWithValue("t", table);
        check.Parameters.AddWithValue("p", privilege);
        return (bool)(await check.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task The_runtime_role_can_insert_and_select_the_inbox_tables_but_not_update_delete_or_truncate_them()
    {
        foreach (var table in new[] { "workflow.assistant_proposals", "workflow.assistant_proposal_decisions" })
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                (await HasAsync(table, privilege)).ShouldBe(expected, $"{privilege} on {table}");
            }
        }

        await using var source = NpgsqlDataSource.Create(server.App);
        await new ProposalStore(source).InsertAsync(Proposal("grant"), Ct);
        foreach (var sql in new[] { "UPDATE workflow.assistant_proposals SET reason = 'x'", "DELETE FROM workflow.assistant_proposals", "UPDATE workflow.assistant_proposal_decisions SET actor = 'x'", "DELETE FROM workflow.assistant_proposal_decisions", "TRUNCATE workflow.assistant_proposals CASCADE" })
        {
            await using var command = source.CreateCommand(sql);
            (await Should.ThrowAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Ct))).SqlState.ShouldBe("42501", sql);
        }
    }

    [Fact]
    public async Task A_proposal_is_stored_once_per_key_and_day_read_back_with_its_decision_and_listed_pending_until_decided()
    {
        await using var source = NpgsqlDataSource.Create(server.App);
        var store = new ProposalStore(source);
        var id = (await store.InsertAsync(Proposal("once"), Ct))!.Value;
        var again = await store.InsertAsync(Proposal("once"), Ct);

        var pendingBefore = await store.PendingAsync(DateTimeOffset.UtcNow, Ct);
        var first = await store.InsertAsync(new NewProposalDecision(id, "rejected", Owner, false, null, null, "no", null), Ct);
        var second = await store.InsertAsync(new NewProposalDecision(id, "accepted", Owner, false, null, null, null, "basket:x"), Ct);
        var read = (await store.GetAsync(id, Ct))!;

        again.ShouldBeNull();
        pendingBefore.ShouldContain(p => p.Id == id);
        (first.Created, second.Created).ShouldBe((true, false));
        second.Decision.Decision.ShouldBe("rejected");
        read.Decision!.Reason.ShouldBe("no");
        (await store.PendingAsync(DateTimeOffset.UtcNow, Ct)).ShouldNotContain(p => p.Id == id);
        (await store.RejectedSinceAsync(read.DedupeKey, DateTimeOffset.UtcNow.AddDays(-1), Ct)).ShouldBeTrue();
        (await store.RejectedSinceAsync(read.DedupeKey, DateTimeOffset.UtcNow.AddDays(1), Ct)).ShouldBeFalse();
        (await store.ListAsync("basket_add", 5, Ct))[0].Id.ShouldBe(id);
    }

    [Fact]
    public async Task Rows_that_break_a_rule_of_the_tables_are_refused()
    {
        await using var source = NpgsqlDataSource.Create(server.App);
        var store = new ProposalStore(source);

        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertAsync(Proposal("badkind", kind: "track_instrument"), Ct))).SqlState.ShouldBe("23514");
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertAsync(Proposal("blank", reason: "  "), Ct))).SqlState.ShouldBe("23514");
        var id = (await store.InsertAsync(Proposal("rules"), Ct))!.Value;
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertAsync(new NewProposalDecision(id, "accepted", Owner, false, null, null, null, null), Ct))).SqlState.ShouldBe("23514");
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertAsync(new NewProposalDecision(id, "accepted", Owner, true, null, null, null, "x"), Ct))).SqlState.ShouldBe("23514");
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertAsync(new NewProposalDecision(id, "accepted", "system:auto-accept", false, null, null, null, "x"), Ct))).SqlState.ShouldBe("23514");
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertAsync(new NewProposalDecision(id + 1000, "rejected", Owner, false, null, null, null, null), Ct))).SqlState.ShouldBe("23503");
        var before = await store.CountAutoBetweenAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), Ct);
        (await store.InsertAsync(new NewProposalDecision(id, "accepted", "system:auto-accept", true, 1, new string('a', 64), null, "hypothesis:1"), Ct)).Created.ShouldBeTrue();
        (await store.CountAutoBetweenAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), Ct)).ShouldBe(before + 1);
    }

    [Fact]
    public async Task The_decision_lock_serialises_two_holders()
    {
        await using var source = NpgsqlDataSource.Create(server.App);
        var store = new ProposalStore(source);
        var first = await store.LockAsync(Ct);
        var second = store.LockAsync(Ct).AsTask();

        await Task.Delay(300, Ct);
        second.IsCompleted.ShouldBeFalse();
        await first.DisposeAsync();
        await using var taken = await second.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    [Fact]
    public async Task The_hypothesis_log_check_names_source_model()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var check = new NpgsqlCommand("SELECT pg_get_constraintdef(c.oid) FROM pg_constraint c WHERE c.conname = 'ck_hypotheses_shape' AND c.conrelid = 'research.hypotheses'::regclass", connection);
        var definition = (string)(await check.ExecuteScalarAsync(Ct))!;
        await using var width = new NpgsqlCommand("SELECT character_maximum_length FROM information_schema.columns WHERE table_schema = 'research' AND table_name = 'hypotheses' AND column_name = 'source'", connection);

        definition.ShouldContain("'model'");
        definition.ShouldContain("'owner'");
        definition.ShouldContain("'grid'");
        definition.ShouldNotContain("'foo'");
        ((int)(await width.ExecuteScalarAsync(Ct))!).ShouldBeGreaterThanOrEqualTo("model".Length);
    }
}
