using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Filings;
using XDrishti.Domain.Filings;
using XDrishti.Infrastructure.FilingArchive;
using XDrishti.Infrastructure.Filings;

namespace XDrishti.Api.IntegrationTests.Filings;

/// <summary>
/// P15-03 on a real TimescaleDB (criteria 5 to 8): the parse step over a real store folder (idempotent, pdf as not_xbrl, a hostile file rejected, one transaction per filing), the per-basis read from stored rows at the instants of the hand example, and the facts route.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ResultFactsDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateTimeOffset StandaloneTs = new(2026, 7, 24, 16, 12, 0, TimeSpan.FromMinutes(330));
    private static readonly DateTimeOffset ConsolidatedTs = new(2026, 7, 24, 16, 40, 0, TimeSpan.FromMinutes(330));
    private static readonly DateTimeOffset RestatementTs = new(2026, 8, 30, 10, 0, 0, TimeSpan.FromMinutes(330));

    private Guid _reliance;
    private string _root = string.Empty;
    private IOptions<FilingsOptions> _options = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _reliance = await SeedAsync("RELIANCE");
        _root = Path.Combine(Path.GetTempPath(), "xd-result-facts-it-" + Guid.NewGuid().ToString("N"));
        _options = Options.Create(new FilingsOptions { InboxPath = Path.Combine(_root, "inbox"), FilesPath = Path.Combine(_root, "files") });
    }

    public override async ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }

        await base.DisposeAsync();
    }

    private static string Xml(string basis, string revenue) =>
        $"<xbrli:xbrl xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" xmlns:in=\"http://synthetic.example/taxonomy/in-results\"><xbrli:unit id=\"u\"><xbrli:measure>iso4217:INR</xbrli:measure></xbrli:unit>"
        + "<xbrli:context id=\"c\"><xbrli:entity><xbrli:identifier scheme=\"x\">F</xbrli:identifier></xbrli:entity><xbrli:period><xbrli:startDate>2026-04-01</xbrli:startDate><xbrli:endDate>2026-06-30</xbrli:endDate></xbrli:period></xbrli:context>"
        + $"<in:NatureOfReportStandaloneConsolidated contextRef=\"c\">{basis}</in:NatureOfReportStandaloneConsolidated><in:RevenueFromOperations contextRef=\"c\" unitRef=\"u\">{revenue}</in:RevenueFromOperations></xbrli:xbrl>";

    private async Task<Guid> StoreAsync(string content, string name, DateTimeOffset ts)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var filing = new NewFiling(_reliance, FilingKind.Results, FilingTestKit.Q1, ts.ToUniversalTime(), BroadcastState.Ok, null, FilingTestKit.Ingested, sha, bytes.Length, name);
        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IFilingStore>().InsertAsync(filing, Ct)).Outcome.ShouldBe(FilingInsertOutcome.Stored);
        (await new FilingFiles(_options).CopyAsync(new MemoryStream(bytes), sha, bytes.Length, Ct)).IsSuccess.ShouldBeTrue();
        return filing.Id;
    }

    private async Task<ParseResultsSummary> ParseAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var handler = new ParseResultsHandler(
            scope.ServiceProvider.GetRequiredService<IResultFactsStore>(), new FilingFiles(_options), scope.ServiceProvider.GetRequiredService<IResultsXbrlReader>(), TimeProvider.System, NullLogger<ParseResultsHandler>.Instance);
        return (await handler.HandleAsync(new ParseResultsCommand(), Ct)).Value;
    }

    private async Task<ResultFactsAnswer> AtAsync(DateTimeOffset t)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IResultFactsAsOf>().Facts(_reliance, t, Ct);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task The_parse_step_stores_every_outcome_once_and_a_second_run_parses_nothing()
    {
        var standalone = await StoreAsync(Xml("Standalone", "3000"), "s.xml", StandaloneTs);
        await StoreAsync("%PDF-1.4 fake", "s.pdf", StandaloneTs.AddMinutes(1));
        await StoreAsync("<!DOCTYPE x [<!ENTITY a \"b\">]><x>&a;</x>", "hostile.xml", StandaloneTs.AddMinutes(2));
        await StoreAsync("<xbrli:xbrl xmlns:xbrli=\"http://www.xbrl.org/2003/instance\"/>", "empty.xml", StandaloneTs.AddMinutes(3));

        var first = await ParseAsync();
        var second = await ParseAsync();

        (first.Pending, first.Parsed, first.NotXbrl, first.Rejected, first.NoFacts, first.Failures).ShouldBe((4, 1, 1, 1, 1, 0));
        (second.Pending, second.Parsed).ShouldBe((0, 0));
        (await ScalarAsync<long>("SELECT count(*) FROM filings.result_parses")).ShouldBe(4);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.result_facts")).ShouldBe(8);
        (await ScalarAsync<string>("SELECT reason FROM filings.result_parses WHERE outcome = 'rejected'")).ShouldBe("dtd_prohibited");
        (await ScalarAsync<string>("SELECT parser_version FROM filings.result_parses WHERE filing_id = @id", ("id", standalone))).ShouldBe("xbrl-1-synthetic");
        (await ScalarAsync<decimal>("SELECT value FROM filings.result_facts WHERE filing_id = @id AND field_code = 'revenue' AND span = 'q' AND basis = 'standalone'", ("id", standalone))).ShouldBe(3000m);
    }

    [Fact]
    public async Task A_stored_file_that_changed_on_disk_is_a_failure_left_for_the_next_run_and_nothing_is_stored()
    {
        var id = await StoreAsync(Xml("Standalone", "3000"), "s.xml", StandaloneTs);
        var sha = await ScalarAsync<string>("SELECT content_sha256 FROM filings.filings WHERE id = @id", ("id", id));
        await File.WriteAllTextAsync(Path.Combine(_root, "files", sha[..2], sha), "<changed/>", Ct);

        var summary = await ParseAsync();

        (summary.Failures, summary.Parsed).ShouldBe((1, 0));
        (await ScalarAsync<long>("SELECT count(*) FROM filings.result_parses")).ShouldBe(0);
    }

    [Fact]
    public async Task One_filings_rows_are_one_transaction_a_bad_row_leaves_no_parse_row_and_no_half_set()
    {
        var id = await StoreAsync(Xml("Standalone", "3000"), "s.xml", StandaloneTs);
        var bad = new ParsedResults("parsed", null, [ParsedFact.Of("revenue", "q", "consolidated", 1m), ParsedFact.Of("pat", "weekly", "consolidated", 2m)], new Dictionary<string, int>());
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IResultFactsStore>();

        await Should.ThrowAsync<PostgresException>(async () => await store.SaveAsync(id, "xbrl-1-synthetic", bad, DateTimeOffset.UtcNow, Ct));

        (await ScalarAsync<long>("SELECT count(*) FROM filings.result_parses")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.result_facts")).ShouldBe(0);
        (await store.PendingAsync("xbrl-1-synthetic", 10, Ct)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_same_filing_saved_twice_for_a_version_is_one_set_and_another_version_adds_its_own()
    {
        var id = await StoreAsync(Xml("Standalone", "3000"), "s.xml", StandaloneTs);
        var parsed = new ParsedResults("parsed", null, [ParsedFact.Of("revenue", "q", "consolidated", 1m)], new Dictionary<string, int>());
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IResultFactsStore>();

        var first = await store.SaveAsync(id, "xbrl-1-synthetic", parsed, DateTimeOffset.UtcNow, Ct);
        var again = await store.SaveAsync(id, "xbrl-1-synthetic", parsed, DateTimeOffset.UtcNow, Ct);
        var later = await store.SaveAsync(id, "xbrl-2", parsed, DateTimeOffset.UtcNow, Ct);

        (first, again, later).ShouldBe((true, false, true));
        (await ScalarAsync<long>("SELECT count(*) FROM filings.result_facts")).ShouldBe(2);
    }

    [Fact]
    public async Task The_per_basis_read_from_stored_rows_follows_the_hand_example_at_each_instant()
    {
        await StoreAsync(Xml("Standalone", "3000"), "s.xml", StandaloneTs);
        await StoreAsync(Xml("Consolidated", "5000"), "c.xml", ConsolidatedTs);
        await StoreAsync(Xml("Consolidated", "5100"), "c2.xml", RestatementTs);
        await ParseAsync();

        var early = await AtAsync(new DateTimeOffset(2026, 7, 24, 16, 30, 0, TimeSpan.FromMinutes(330)));
        var both = await AtAsync(new DateTimeOffset(2026, 7, 24, 16, 45, 0, TimeSpan.FromMinutes(330)));
        var late = await AtAsync(new DateTimeOffset(2026, 8, 30, 10, 5, 0, TimeSpan.FromMinutes(330)));
        var between = await AtAsync(new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.FromMinutes(330)));

        string Revenue(ResultFactsAnswer a) => string.Join(",", a.Facts.Where(f => f.Field == "revenue").Select(f => $"{f.Basis}={f.Value?.ToString(CultureInfo.InvariantCulture)}"));
        Revenue(early).ShouldBe("standalone=3000");
        Revenue(both).ShouldBe("consolidated=5000,standalone=3000");
        Revenue(between).ShouldBe("consolidated=5000,standalone=3000");
        Revenue(late).ShouldBe("consolidated=5100,standalone=3000");
        early.Get(FilingTestKit.Q1, "eps_basic", "q", "standalone").Reason.ShouldBe("not_in_file");
    }

    [Fact]
    public async Task An_undated_envelopes_facts_are_never_read_only_counted()
    {
        await StoreAsync(Xml("Standalone", "3000"), "s.xml", StandaloneTs);
        var bytes = Encoding.UTF8.GetBytes(Xml("Consolidated", "999"));
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IFilingStore>().InsertAsync(new NewFiling(_reliance, FilingKind.Results, FilingTestKit.Q1, null, BroadcastState.Unavailable, "date_only", FilingTestKit.Ingested, sha, bytes.Length, "u.xml"), Ct);
        }

        await new FilingFiles(_options).CopyAsync(new MemoryStream(bytes), sha, bytes.Length, Ct);
        var summary = await ParseAsync();
        var answer = await AtAsync(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));

        summary.Parsed.ShouldBe(2, "an undated envelope is still parsed and its facts kept");
        answer.UndatedCount.ShouldBe(1);
        answer.Facts.ShouldNotContain(f => f.Basis == "consolidated");
    }

    [Fact]
    public async Task The_facts_route_answers_not_parsed_then_the_outcome_and_the_facts_and_404_for_an_unknown_id()
    {
        var id = await StoreAsync(Xml("Standalone", "3000"), "s.xml", StandaloneTs);

        var before = await GetAsync($"/api/filings/{id}/facts");
        await ParseAsync();
        var after = await GetAsync($"/api/filings/{id}/facts");
        var unknown = await GetAsync($"/api/filings/{Guid.NewGuid()}/facts");

        before.Body.GetProperty("outcome").GetString().ShouldBe("not_parsed");
        after.Status.ShouldBe(HttpStatusCode.OK);
        (after.Body.GetProperty("outcome").GetString(), after.Body.GetProperty("parserVersion").GetString(), after.Body.GetProperty("factCount").GetInt32()).ShouldBe(("parsed", "xbrl-1-synthetic", 8));
        var revenue = after.Body.GetProperty("facts").EnumerateArray().Single(f => f.GetProperty("field").GetString() == "revenue");
        (revenue.GetProperty("state").GetString(), revenue.GetProperty("value").GetDecimal(), revenue.GetProperty("unit").GetString(), revenue.GetProperty("span").GetString(), revenue.GetProperty("basis").GetString()).ShouldBe(("value", 3000m, "inr", "q", "standalone"));
        after.Body.GetProperty("facts").EnumerateArray().Single(f => f.GetProperty("field").GetString() == "pat").GetProperty("reason").GetString().ShouldBe("not_in_file");
        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
        unknown.Body.GetProperty("code").GetString().ShouldBe("filings.not_found");
        after.Body.ToString().ShouldNotContain(_root);
        after.Body.ToString().ShouldNotContain("s.xml");
    }

    [Fact]
    public async Task The_facts_route_needs_a_session()
    {
        using var anonymous = Factory.CreateClient();

        using var response = await anonymous.GetAsync(new Uri($"/api/filings/{Guid.NewGuid()}/facts", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
