using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Spec P7-15 on a real database: the digest is built from the stored records without writing anything else, an old week is never stored from today's data, a stored digest is one per ISO week, and the endpoints read it.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class WeeklyDigestApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string[] Tables =
    [
        "research.lessons", "research.hypotheses", "research.learning_changelog", "research.experiments", "research.sim_runs", "research.cell_stats", "research.model_versions", "research.model_designations", "workflow.approvals", "trading.plans", "trading.plan_items",
    ];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.digests");
    }

    private async Task<long> CountAsync(string table)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<Dictionary<string, long>> CountsAsync()
    {
        var counts = new Dictionary<string, long>();
        foreach (var table in Tables)
        {
            counts[table] = await CountAsync(table);
        }

        return counts;
    }

    private async Task<XDrishti.Domain.Common.Result<DigestView>> BuildAsync(string? week, bool store)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<BuildDigestCommand, DigestView>>().HandleAsync(new BuildDigestCommand(week, store, "cli:xd"), Ct);
    }

    /// <summary>A digest of a past week made from a fixture, not from today's state: the store is exercised, the handler's rule keeps real builds of old weeks out.</summary>
    private static NewDigest Fixture(string week)
    {
        var input = new DigestInput(DigestWeek.TryParse(week)!.Value, new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.FromMinutes(330)), null, [], [], [], [], 0, 0, [], 0, [], null, [], [], [], []);
        return new NewDigest(DigestBuilder.Build(input), "test", new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.FromMinutes(330)));
    }

    [Fact]
    public async Task Before_any_digest_the_newest_is_not_found_and_the_list_is_empty()
    {
        using var newest = await Client.GetAsync(new Uri("/api/learning/digest", UriKind.Relative), Ct);
        using var list = await Client.GetAsync(new Uri("/api/learning/digests", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri("/api/learning/digest?week=nope", UriKind.Relative), Ct);
        using var badLimit = await Client.GetAsync(new Uri("/api/learning/digests?limit=0", UriKind.Relative), Ct);

        newest.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        badLimit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_dry_run_builds_from_the_stored_records_and_writes_nothing_at_all()
    {
        var before = await CountsAsync();

        var result = await BuildAsync("2026-W01", store: false);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Id.ShouldBeNull();
        result.Value.Text.ShouldContain("not as of the end of the week");
        (await CountsAsync()).ShouldBe(before);
        (await CountAsync("research.digests")).ShouldBe(0);
    }

    [Fact]
    public async Task An_old_week_cannot_be_stored_from_todays_state()
    {
        var result = await BuildAsync("2026-W01", store: true);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        (await CountAsync("research.digests")).ShouldBe(0);
    }

    [Fact]
    public async Task A_stored_digest_is_one_per_week_carries_the_closing_line_and_is_read_by_the_endpoints()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IDigestStore>();
            var first = await store.InsertAsync(Fixture("2026-W01"), Ct);
            var second = await store.InsertAsync(Fixture("2026-W01"), Ct);

            first.Created.ShouldBeTrue();
            second.Created.ShouldBeFalse();
            second.Stored.Id.ShouldBe(first.Stored.Id);
        }

        (await CountAsync("research.digests")).ShouldBe(1);
        await using (var connection = await OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT sections_json FROM research.digests WHERE iso_week = '2026-W01'";
            var sections = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(Ct))!).RootElement;
            sections.GetArrayLength().ShouldBe(8);
            foreach (var section in sections.EnumerateArray())
            {
                var lines = section.GetProperty("Lines");
                lines[lines.GetArrayLength() - 1].GetString().ShouldBe(DigestBuilder.SectionNote);
            }
        }

        using var one = await Client.GetAsync(new Uri("/api/learning/digest?week=2026-W01", UriKind.Relative), Ct);
        using var newest = await Client.GetAsync(new Uri("/api/learning/digest", UriKind.Relative), Ct);
        using var list = await Client.GetAsync(new Uri("/api/learning/digests", UriKind.Relative), Ct);
        var json = await one.Content.ReadFromJsonAsync<JsonElement>(Ct);
        json.GetProperty("isoWeek").GetString().ShouldBe("2026-W01");
        json.GetProperty("text").GetString()!.ShouldContain("Built at 2026-01-10 10:00 IST.");
        json.GetProperty("sections").GetArrayLength().ShouldBe(8);
        newest.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task The_database_refuses_a_digest_that_does_not_say_nothing_was_applied()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO research.digests (iso_week, week_start, week_end, text, sections_json, built_by, created_at) VALUES ('2026-W02', '2026-01-05', '2026-01-11', 'applied it', '[]', 'x', now())";

        var failure = await Should.ThrowAsync<Npgsql.PostgresException>(async () => await command.ExecuteNonQueryAsync(Ct));

        failure.SqlState.ShouldBe(Npgsql.PostgresErrorCodes.CheckViolation);
    }
}
