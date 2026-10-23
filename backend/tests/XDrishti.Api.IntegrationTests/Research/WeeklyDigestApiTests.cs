using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Spec P7-15 on a real database: the digest is built from the stored records, stored once per ISO week, read by the endpoints, and nothing else is written.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class WeeklyDigestApiTests(ApiFactory factory) : DataTestBase(factory)
{
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

    private async Task<DigestView> BuildAsync(string? week)
    {
        using var scope = Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<BuildDigestCommand, DigestView>>().HandleAsync(new BuildDigestCommand(week, false, "cli:xd"), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
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
    public async Task A_digest_is_built_stored_once_per_week_and_read_back_with_nothing_else_written()
    {
        var lessons = await CountAsync("research.lessons");
        var hypotheses = await CountAsync("research.hypotheses");
        var changelog = await CountAsync("research.learning_changelog");
        var approvals = await CountAsync("workflow.approvals");
        var plans = await CountAsync("trading.plans");

        var first = await BuildAsync("2026-W01");
        var second = await BuildAsync("2026-W01");

        first.Repeated.ShouldBeFalse();
        second.Repeated.ShouldBeTrue();
        second.Id.ShouldBe(first.Id);
        second.Text.ShouldBe(first.Text);
        (await CountAsync("research.digests")).ShouldBe(1);
        (await CountAsync("research.lessons")).ShouldBe(lessons);
        (await CountAsync("research.hypotheses")).ShouldBe(hypotheses);
        (await CountAsync("research.learning_changelog")).ShouldBe(changelog);
        (await CountAsync("workflow.approvals")).ShouldBe(approvals);
        (await CountAsync("trading.plans")).ShouldBe(plans);

        using var one = await Client.GetAsync(new Uri("/api/learning/digest?week=2026-W01", UriKind.Relative), Ct);
        using var newest = await Client.GetAsync(new Uri("/api/learning/digest", UriKind.Relative), Ct);
        using var list = await Client.GetAsync(new Uri("/api/learning/digests", UriKind.Relative), Ct);
        var json = await one.Content.ReadFromJsonAsync<JsonElement>(Ct);
        json.GetProperty("isoWeek").GetString().ShouldBe("2026-W01");
        json.GetProperty("text").GetString()!.ShouldContain("Nothing was applied");
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
