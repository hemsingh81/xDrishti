using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Lab;

/// <summary>The Lab API of P5-01 end to end against a real database: validate, create and version, the built-ins, the reads, the file round trip, the clone and the races.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private async Task<HttpClient> ClientAsync() => (await factory.SignInAsync(Ct)).Client;

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<JsonElement> ValidateAsync(HttpClient client, string text)
    {
        using var response = await client.PostAsJsonAsync(Url("/api/lab/validate"), new { text, format = "yaml" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await JsonOf(response);
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string text, object? extra = null) =>
        client.PostAsJsonAsync(Url("/api/lab/strategies"), extra ?? new { text }, Ct);

    private static async Task<int> VersionCountAsync(HttpClient client, string id)
    {
        using var response = await client.GetAsync(Url($"/api/lab/strategies/{id}"), Ct);
        return response.StatusCode == HttpStatusCode.NotFound ? 0 : (await JsonOf(response)).GetProperty("versions").GetArrayLength();
    }

    // criterion 1

    [Fact]
    public async Task Validate_returns_no_problem_and_the_hash_and_the_hash_ignores_layout_and_key_order()
    {
        var client = await ClientAsync();
        var id = LabTexts.NewId();
        var text = LabTexts.Valid(id);
        var reshaped = "# comment\r\n" + text.Replace("  id: " + id + "\n  style: intraday\n", "  style: intraday   # same\n  id: " + id + "\n").Replace("\n", "\r\n") + "\r\n\r\n";
        reshaped.ShouldNotBe(text);

        var first = await ValidateAsync(client, text);
        var second = await ValidateAsync(client, reshaped);

        first.GetProperty("valid").GetBoolean().ShouldBeTrue();
        first.GetProperty("hash").GetString()!.Length.ShouldBe(64);
        first.GetProperty("id").GetString().ShouldBe(id);
        first.GetProperty("sides").EnumerateArray().Select(e => e.GetString()).ShouldBe(["BUY", "SELL"]);
        first.GetProperty("problems").GetArrayLength().ShouldBe(0);
        second.GetProperty("hash").GetString().ShouldBe(first.GetProperty("hash").GetString());
    }

    [Fact]
    public async Task Validate_writes_nothing()
    {
        var client = await ClientAsync();
        var id = LabTexts.NewId();

        await ValidateAsync(client, LabTexts.Valid(id));

        (await VersionCountAsync(client, id)).ShouldBe(0);
    }

    // criterion 2

    public static TheoryData<string, string, string, string> Mutations => new()
    {
        { "misspelt word in buy.trigger[1]", "close[1] > vwap[1] and", "clse[1] > vwap[1] and", "strategy.lint.unknown_name" },
        { "a fifth param", "    rr: 2\n", "    rr: 2\n    a: 1\n    b: 2\n    c: 3\n", "strategy.model.too_many_params" },
        { "a swing SELL", "style: intraday", "style: swing", "strategy.model.swing_sell_unsupported" },
        { "a filter with no data", "  time_rules:", "  filters: [no_such_filter]\n  time_rules:", "strategy.model.unknown_filter" },
        { "an unknown key", "  entry:\n    type: at_close\n  stop", "  entri: 1\n  entry:\n    type: at_close\n  stop", "file.unknown_field" },
        { "a wrong type", "style: intraday", "style: [intraday]", "file.wrong_type" },
        { "a duplicate key", "style: intraday", "style: intraday\n  style: swing", "file.duplicate_key" },
        { "an alias bomb", "style: intraday", "style: &a intraday\n  sides: [*a, *a]", "file.alias_refused" },
        { "an oversize scalar", "style: intraday", "style: " + new string('a', 5000), "file.scalar_too_long" },
        { "a time that is not a time", "square_off: \"15:15\"", "square_off: \"25:99\"", "file.bad_time" },
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task Each_mutation_is_answered_with_its_located_problem_and_never_a_500(string name, string before, string after, string code)
    {
        var client = await ClientAsync();
        var text = LabTexts.Valid(LabTexts.NewId()).Replace(before, after, StringComparison.Ordinal);
        text.ShouldNotBe(LabTexts.Valid("x"), name);

        var answer = await ValidateAsync(client, text);

        answer.GetProperty("valid").GetBoolean().ShouldBeFalse(name);
        answer.TryGetProperty("hash", out var hash).ShouldBeTrue();
        hash.ValueKind.ShouldBe(JsonValueKind.Null);
        var problem = answer.GetProperty("problems").EnumerateArray().First(p => p.GetProperty("code").GetString() == code);
        problem.GetProperty("severity").GetString().ShouldBe("error");
        problem.GetProperty("message").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_text_over_the_limit_is_a_problem_with_a_code_and_the_empty_text_is_too()
    {
        var client = await ClientAsync();

        var big = await ValidateAsync(client, new string('x', 70_000));
        var empty = await ValidateAsync(client, string.Empty);

        big.GetProperty("problems")[0].GetProperty("code").GetString().ShouldBe("lab.text_too_large");
        empty.GetProperty("valid").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Another_format_than_yaml_is_a_400()
    {
        var client = await ClientAsync();

        using var response = await client.PostAsJsonAsync(Url("/api/lab/validate"), new { text = "a", format = "json" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task The_endpoints_need_the_owner_session()
    {
        using var response = await factory.CreateAnonymousClient().PostAsJsonAsync(Url("/api/lab/validate"), new { text = "a" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // criterion 3 and 4

    [Fact]
    public async Task Create_stores_version_1_and_the_same_text_again_is_the_same_version_and_writes_nothing()
    {
        var client = await ClientAsync();
        var id = LabTexts.NewId();
        var text = LabTexts.Valid(id);

        using var first = await CreateAsync(client, text);
        using var again = await CreateAsync(client, text);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        first.Headers.Location!.ToString().ShouldBe($"/api/lab/strategies/{id}/versions/1");
        var created = await JsonOf(first);
        created.GetProperty("version").GetInt32().ShouldBe(1);
        created.GetProperty("parentHash").ValueKind.ShouldBe(JsonValueKind.Null);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonOf(again)).GetProperty("hash").GetString().ShouldBe(created.GetProperty("hash").GetString());
        (await VersionCountAsync(client, id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_changed_text_becomes_the_next_version_with_the_previous_one_as_parent_and_the_version_of_the_text_must_match()
    {
        var client = await ClientAsync();
        var id = LabTexts.NewId();
        using var one = await CreateAsync(client, LabTexts.Valid(id));
        var hash1 = (await JsonOf(one)).GetProperty("hash").GetString();

        using var wrong = await CreateAsync(client, LabTexts.Changed(id).Replace("  id: " + id + "\n", "  id: " + id + "\n  version: 1\n", StringComparison.Ordinal));
        using var right = await CreateAsync(client, LabTexts.Changed(id).Replace("  id: " + id + "\n", "  id: " + id + "\n  version: 2\n", StringComparison.Ordinal), new { text = LabTexts.Changed(id).Replace("  id: " + id + "\n", "  id: " + id + "\n  version: 2\n", StringComparison.Ordinal), note = "tightened" });

        wrong.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonOf(wrong)).GetProperty("code").GetString().ShouldBe("lab.version_exists");
        right.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await JsonOf(right);
        created.GetProperty("version").GetInt32().ShouldBe(2);
        created.GetProperty("parentHash").GetString().ShouldBe(hash1);
        (await VersionCountAsync(client, id)).ShouldBe(2);
    }

    // criterion 5

    [Fact]
    public async Task An_invalid_text_is_a_422_with_the_problems_and_no_row()
    {
        var client = await ClientAsync();
        var id = LabTexts.NewId();

        using var response = await CreateAsync(client, LabTexts.Valid(id).Replace("close[1] > vwap[1] and", "clse[1] > vwap[1] and", StringComparison.Ordinal));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await JsonOf(response);
        body.GetProperty("code").GetString().ShouldBe("lab.invalid_strategy");
        body.GetProperty("problems").EnumerateArray().ShouldContain(p => p.GetProperty("code").GetString() == "strategy.lint.unknown_name");
        (await VersionCountAsync(client, id)).ShouldBe(0);
    }

    // criterion 6 and the seed flag of the minors

    [Fact]
    public async Task A_built_in_can_be_read_downloaded_and_cloned_and_never_written_by_a_post()
    {
        var client = await ClientAsync();
        await StrategySeeding.SeedAsync(factory.Services, Ct);

        using var write = await CreateAsync(client, LabTexts.Valid("ORB"));
        using var seedFlag = await CreateAsync(client, LabTexts.Valid("ORB"), new { text = LabTexts.Valid("ORB"), seed = true, builtIn = true });
        using var read = await client.GetAsync(Url("/api/lab/strategies/ORB/versions/1"), Ct);
        using var file = await client.GetAsync(Url("/api/lab/strategies/ORB/versions/1/file"), Ct);
        using var clone = await client.PostAsync(Url("/api/lab/strategies/ORB/versions/1/clone"), null, Ct);

        write.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonOf(write)).GetProperty("code").GetString().ShouldBe("lab.builtin_readonly");
        seedFlag.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonOf(read)).GetProperty("problemsFree").GetBoolean().ShouldBeTrue();
        file.StatusCode.ShouldBe(HttpStatusCode.OK);
        clone.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cloned = await JsonOf(clone);
        cloned.GetProperty("id").GetString().ShouldBe("ORB_COPY");
        cloned.GetProperty("text").GetString()!.ShouldContain("id: ORB_COPY");
        (await ValidateAsync(client, cloned.GetProperty("text").GetString()!)).GetProperty("valid").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_new_family_is_never_a_built_in_whatever_the_body_says()
    {
        var client = await ClientAsync();
        var id = LabTexts.NewId();

        using var response = await CreateAsync(client, LabTexts.Valid(id), new { text = LabTexts.Valid(id), seed = true, builtIn = true });
        using var list = await client.GetAsync(Url("/api/lab/strategies"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await JsonOf(list)).EnumerateArray().Single(f => f.GetProperty("id").GetString() == id).GetProperty("builtIn").GetBoolean().ShouldBeFalse();
    }

    // criterion 7

    [Fact]
    public async Task The_downloaded_file_posted_to_validate_gives_the_same_hash()
    {
        var client = await ClientAsync();
        var id = LabTexts.NewId();
        using var created = await CreateAsync(client, LabTexts.Valid(id));
        var hash = (await JsonOf(created)).GetProperty("hash").GetString();

        using var file = await client.GetAsync(Url($"/api/lab/strategies/{id}/versions/1/file"), Ct);

        file.StatusCode.ShouldBe(HttpStatusCode.OK);
        file.Content.Headers.ContentType?.MediaType.ShouldBe("application/yaml");
        file.Content.Headers.ContentDisposition?.DispositionType.ShouldBe("attachment");
        file.Content.Headers.ContentDisposition?.FileName.ShouldBe($"{id}-v1.yaml");
        var text = await file.Content.ReadAsStringAsync(Ct);
        text.ShouldBe(LabTexts.Valid(id));
        (await ValidateAsync(client, text)).GetProperty("hash").GetString().ShouldBe(hash);
    }

    // criterion 8

    [Fact]
    public async Task The_reads_return_what_is_stored_and_a_missing_id_or_version_is_a_404()
    {
        var client = await ClientAsync();
        var id = LabTexts.NewId();
        using var created = await CreateAsync(client, LabTexts.Valid(id));
        using var second = await CreateAsync(client, LabTexts.Changed(id), new { text = LabTexts.Changed(id), note = "second" });

        using var list = await client.GetAsync(Url("/api/lab/strategies"), Ct);
        using var detail = await client.GetAsync(Url($"/api/lab/strategies/{id}"), Ct);
        using var version = await client.GetAsync(Url($"/api/lab/strategies/{id}/versions/2"), Ct);
        using var missingId = await client.GetAsync(Url("/api/lab/strategies/NO_SUCH_ONE"), Ct);
        using var missingVersion = await client.GetAsync(Url($"/api/lab/strategies/{id}/versions/9"), Ct);
        using var missingFile = await client.GetAsync(Url($"/api/lab/strategies/{id}/versions/9/file"), Ct);
        using var missingClone = await client.PostAsync(Url("/api/lab/strategies/NO_SUCH_ONE/versions/1/clone"), null, Ct);

        var family = (await JsonOf(list)).EnumerateArray().Single(f => f.GetProperty("id").GetString() == id);
        family.GetProperty("latestVersion").GetInt32().ShouldBe(2);
        family.GetProperty("versionCount").GetInt32().ShouldBe(2);
        (await JsonOf(detail)).GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("version").GetInt32()).ShouldBe([1, 2]);
        var v2 = await JsonOf(version);
        v2.GetProperty("note").GetString().ShouldBe("second");
        v2.GetProperty("text").GetString().ShouldBe(LabTexts.Changed(id));
        foreach (var missing in new[] { missingId, missingVersion, missingFile, missingClone })
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await JsonOf(missing)).GetProperty("code").GetString().ShouldBe("lab.not_found");
        }
    }

    [Fact]
    public async Task The_schema_is_served_as_json_schema()
    {
        var client = await ClientAsync();

        using var response = await client.GetAsync(Url("/api/lab/schema"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/schema+json");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("https://json-schema.org/draft/2020-12/schema");
    }

    [Fact]
    public async Task A_clone_of_a_clone_names_the_hash_of_the_one_it_came_from_and_stores_nothing()
    {
        var client = await ClientAsync();
        var id = LabTexts.NewId();
        using var created = await CreateAsync(client, LabTexts.Valid(id));
        var hash = (await JsonOf(created)).GetProperty("hash").GetString();
        var other = LabTexts.NewId();

        using var clone = await client.PostAsJsonAsync(Url($"/api/lab/strategies/{id}/versions/1/clone"), new { newId = other }, Ct);
        var cloned = await JsonOf(clone);
        using var stored = await CreateAsync(client, cloned.GetProperty("text").GetString()!, new { text = cloned.GetProperty("text").GetString(), parentHash = cloned.GetProperty("parentHash").GetString() });
        using var again = await client.PostAsJsonAsync(Url($"/api/lab/strategies/{other}/versions/1/clone"), new { }, Ct);

        cloned.GetProperty("parentHash").GetString().ShouldBe(hash);
        stored.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await JsonOf(stored)).GetProperty("parentHash").GetString().ShouldBe(hash);
        (await JsonOf(again)).GetProperty("id").GetString().ShouldBe(other);
        (await VersionCountAsync(client, id)).ShouldBe(1);
    }

    // concurrency

    [Fact]
    public async Task Concurrent_posts_of_the_same_text_leave_one_row_and_one_created()
    {
        var client = await ClientAsync();
        var id = LabTexts.NewId();
        var text = LabTexts.Valid(id);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => CreateAsync(client, text)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.OK);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        (await VersionCountAsync(client, id)).ShouldBe(1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}
