using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Paper;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// Spec P10-03 on a real database: a review is stored once per week and epoch and read back as built, the database refuses a text without the pinned line, the raw paper reads work on an empty epoch, and the four endpoints are GETs that read the record.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PaperReviewDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Pinned = "Paper only: no order was placed and none exists.";

    private async Task<long> EpochAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IGovernanceStore>().CreateFirstEpochAsync(new DateOnly(2099, 1, 5), DateTimeOffset.UtcNow, null, Ct)).Id;
    }

    private static NewPaperReview Review(long epoch, string text = Pinned + "\nbody\n") =>
        new("2099-W02", epoch, DateTimeOffset.UtcNow, "cli:xd", text, [new ReviewSection("standing", "Where it stands", ["a line"], [new ReviewMetric("counted", "Counted", "value", 3, null, 3)], "limits", new ReviewTable(["a"], [["b"]]))]);

    [Fact]
    public async Task A_review_is_stored_once_per_week_and_epoch_and_read_back_as_built()
    {
        var epoch = await EpochAsync();
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPaperReviewStore>();

        var (first, created) = await store.InsertAsync(Review(epoch), Ct);
        var (again, createdAgain) = await store.InsertAsync(Review(epoch, Pinned + "\nanother\n"), Ct);

        created.ShouldBeTrue();
        createdAgain.ShouldBeFalse();
        again.Id.ShouldBe(first.Id);
        again.Review.Text.ShouldBe(Pinned + "\nbody\n");
        again.Review.Sections.Single().Metrics.Single().Number.ShouldBe(3);
        again.Review.Sections.Single().Table!.Rows[0][0].ShouldBe("b");
        (await store.ListAsync(epoch, 10, Ct)).Select(r => r.Review.Week).ShouldBe(["2099-W02"]);
        (await ScalarAsync<long>("SELECT count(*) FROM paper.weekly_reviews")).ShouldBe(1);
    }

    [Fact]
    public async Task The_database_refuses_a_text_without_the_pinned_line_and_a_bad_week()
    {
        var epoch = await EpochAsync();
        await using var connection = await OpenAsync();

        var noPin = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "INSERT INTO paper.weekly_reviews (iso_week, epoch_id, built_at, built_by, text, sections) VALUES ('2099-W03', @e, now(), 'x', 'hello', '[]')", ("e", epoch)));
        var badWeek = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "INSERT INTO paper.weekly_reviews (iso_week, epoch_id, built_at, built_by, text, sections) VALUES ('2099-3', @e, now(), 'x', '" + Pinned + "', '[]')", ("e", epoch)));

        noPin.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        badWeek.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_raw_reads_of_an_empty_epoch_are_empty()
    {
        var epoch = await EpochAsync();
        using var scope = Factory.Services.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IPaperReviewData>();

        (await data.TradesAsync(epoch, Ct)).ShouldBeEmpty();
        (await data.SkipsAsync(epoch, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_endpoints_list_get_dry_run_and_report_discipline_and_refuse_bad_input()
    {
        var epoch = await EpochAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPaperReviewStore>().InsertAsync(Review(epoch), Ct);
        }

        await ResignInAsync();
        var list = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/reviews", UriKind.Relative), Ct);
        var one = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/paper/reviews/2099-W02?epoch={epoch}", UriKind.Relative), Ct);
        var dry = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/review", UriKind.Relative), Ct);
        var discipline = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/discipline", UriKind.Relative), Ct);
        using var missing = await Client.GetAsync(new Uri("/api/paper/reviews/2099-W09", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri("/api/paper/reviews/nonsense", UriKind.Relative), Ct);
        using var future = await Client.GetAsync(new Uri("/api/paper/review?week=2099-W50", UriKind.Relative), Ct);
        using var post = await Client.PostAsync(new Uri("/api/paper/reviews", UriKind.Relative), null, Ct);

        list.GetProperty("items")[0].GetProperty("week").GetString().ShouldBe("2099-W02");
        one.GetProperty("stored").GetBoolean().ShouldBeTrue();
        one.GetProperty("text").GetString().ShouldStartWith(Pinned);
        one.GetProperty("sections")[0].GetProperty("metrics")[0].GetProperty("state").GetString().ShouldBe("value");
        dry.GetProperty("stored").GetBoolean().ShouldBeFalse();
        dry.GetProperty("text").GetString().ShouldStartWith(Pinned);
        discipline.GetProperty("lines").GetArrayLength().ShouldBeGreaterThan(0);
        discipline.GetProperty("lines")[0].GetProperty("category").GetString().ShouldBe("followed");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        future.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        post.StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
    }
}
