using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Exports;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Exports;

/// <summary>
/// Spec P8-20 (ADR 0131) end to end on a real database: with the capability off (the default) every kind is refused before anything is built; with it on the file carries its evidence block, the very numbers of the GET of the same query and the same bytes the second time; the headers,
/// the row cap, the rate rule and the audit row (counts only) are as the spec says; nothing about the owner is in a file.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExportsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string Both = ReportFixture.RunA.RunKey + "," + ReportFixture.RunB.RunKey;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await ReportFixture.SeedAsync(Factory, Ct);
        await ExecuteSqlAsync("DELETE FROM platform.app_settings WHERE key = 'reports.charge_sheet_confirmation'");
        Factory.Services.GetRequiredService<ExportSlot>().Reset();
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, sql);
    }

    private Task OnAsync(string name = "exports") =>
        ExecuteSqlAsync($"INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES ('{name}', true, 'test', 'owner:test@example.test')");

    private async Task<HttpResponseMessage> PostAsync(string kind, object? query = null, string format = "csv")
    {
        var body = new Dictionary<string, object?> { ["format"] = format, ["query"] = query ?? new { runs = Both, lens = "basket" } };
        return await Client.PostAsJsonAsync(new Uri("/api/exports/" + kind, UriKind.Relative), body, Ct);
    }

    private async Task<string> CsvAsync(string kind, object? query = null)
    {
        using var response = await PostAsync(kind, query);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<(HttpStatusCode Status, string Code)> ProblemAsync(string kind, object? query = null, string format = "csv")
    {
        using var response = await PostAsync(kind, query, format);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (response.StatusCode, body.GetProperty("code").GetString()!);
    }

    private Task<long> EventsAsync() => ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'ReportExported'");

    /// <summary>The rows of a file made by the writer (no quoted line breaks occur: the writer removes them): comment lines apart, data rows split by RFC 4180 rules.</summary>
    private static (List<string> Comments, List<List<string>> Rows) Parse(string text)
    {
        var comments = new List<string>();
        var rows = new List<List<string>>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('#'))
            {
                comments.Add(line);
                continue;
            }

            var cells = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        cell.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                }
                else
                {
                    cell.Append(c);
                }
            }

            cells.Add(cell.ToString());
            rows.Add(cells);
        }

        return (comments, rows);
    }

    private static readonly (string Kind, object Query)[] Kinds =
    [
        ("report-groups", new { runs = Both, lens = "basket" }), ("report-pivot", new { runs = Both, lens = "basket,strategy", metric = "total_r" }), ("report-what-works", new { runs = Both }),
        ("report-equity", new { runs = Both, lens = "strategy" }), ("report-trades", new { runs = Both }),
    ];

    [Fact]
    public async Task A_fresh_database_has_exports_off_and_every_kind_is_refused_before_anything_is_built_even_one_that_does_not_exist()
    {
        var caps = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/capabilities", UriKind.Relative), Ct);
        caps.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "exports").GetProperty("enabled").GetBoolean().ShouldBeFalse();

        foreach (var (kind, query) in Kinds)
        {
            (await ProblemAsync(kind, query)).ShouldBe((HttpStatusCode.Forbidden, "capabilities.off"), kind);
        }

        (await ProblemAsync("nonsense")).ShouldBe((HttpStatusCode.Forbidden, "capabilities.off"));
        await OnAsync("learning_moves_ui");
        await OnAsync("autonomy_form");
        (await ProblemAsync("report-groups")).ShouldBe((HttpStatusCode.Forbidden, "capabilities.off"), "another capability does not open it");
        (await EventsAsync()).ShouldBe(0);

        // OFF is never the 429: more refusals than the rate rule allows are still all 403
        for (var i = 0; i < 8; i++)
        {
            (await ProblemAsync("report-groups")).Status.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task A_request_without_a_session_is_refused_and_a_switch_off_again_closes_it()
    {
        await OnAsync();
        (await PostAsync("report-groups")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ExecuteSqlAsync("INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES ('exports', false, 'test', 'owner:test@example.test')");

        (await ProblemAsync("report-groups")).ShouldBe((HttpStatusCode.Forbidden, "capabilities.off"));
        using var anonymous = Factory.CreateClient();
        using var response = await anonymous.PostAsJsonAsync(new Uri("/api/exports/report-groups", UriKind.Relative), new { format = "csv" }, Ct);
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task With_the_capability_on_an_unknown_kind_an_unknown_format_and_an_invalid_query_are_422_with_their_codes_and_write_nothing()
    {
        await OnAsync();

        (await ProblemAsync("cells")).ShouldBe((HttpStatusCode.UnprocessableEntity, "exports.kind_unknown"));
        (await ProblemAsync("experiments")).ShouldBe((HttpStatusCode.UnprocessableEntity, "exports.kind_unknown"));
        (await ProblemAsync("report-groups", format: "html")).ShouldBe((HttpStatusCode.UnprocessableEntity, "exports.format_unknown"));
        (await ProblemAsync("report-groups", new { runs = Both, lens = "planet" })).ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.invalid"));
        (await ProblemAsync("report-groups", new { runs = Both, basis = "net" })).ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.net_not_confirmed"));
        (await ProblemAsync("report-groups", new { runs = Both, basis = "net", chargeSheetConfirmed = true, confirmed = true })).ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.net_not_confirmed"), "a body cannot say the sheet was confirmed");
        (await EventsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_groups_file_has_the_evidence_block_the_numbers_of_the_get_and_the_download_headers()
    {
        await OnAsync();
        var get = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/reports/groups?runs={Both}&lens=basket", UriKind.Relative), Ct);
        using var response = await PostAsync("report-groups");
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().ShouldBe("text/csv; charset=utf-8");
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldNotBeNull().ShouldMatch(@"^xd-report-groups-20261012-[0-9a-f]{8}\.csv$");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();

        var (comments, rows) = Parse(text);
        comments.ShouldContain(c => c.StartsWith("# basis: gross", StringComparison.Ordinal));
        comments.ShouldContain(c => c.StartsWith("# population rule:", StringComparison.Ordinal));
        comments.ShouldContain(c => c.StartsWith("# as-of: ", StringComparison.Ordinal));
        comments.ShouldContain(c => c.Contains("population: n=93", StringComparison.Ordinal));
        comments.ShouldContain(c => c.StartsWith($"# run: {ReportFixture.RunA.RunKey} strategy RPTA universe ", StringComparison.Ordinal));
        comments.Count(c => c.StartsWith("# limit: ", StringComparison.Ordinal)).ShouldBe(get.GetProperty("evidence").GetProperty("limits").GetArrayLength());
        comments.ShouldContain(c => c.StartsWith("# unavailable input: ", StringComparison.Ordinal));
        comments.ShouldContain(c => c.Contains("not additive", StringComparison.Ordinal) || c.Contains("do not add the groups up", StringComparison.Ordinal));
        comments.Last().ShouldBe($"# end of export, rows {rows.Count - 1}");

        var groups = get.GetProperty("groups").EnumerateArray().ToList();
        rows.Count.ShouldBe(1 + groups.Count);
        comments.ShouldContain($"# table-rows: {groups.Count}");
        var header = rows[0];
        var expectancy = header.IndexOf("expectancy_r");
        var key = header.IndexOf("group");
        foreach (var group in groups)
        {
            var row = rows.Skip(1).Single(r => r[key] == group.GetProperty("key").GetString());
            row[expectancy].ShouldBe(group.GetProperty("expectancyR").GetProperty("number").GetDouble().ToString("R", CultureInfo.InvariantCulture));
            row[header.IndexOf("n")].ShouldBe(group.GetProperty("n").GetInt32().ToString(CultureInfo.InvariantCulture));
        }

        // the hash in the evidence is the hash of the table bytes, and the name carries its first 8 hex characters
        var table = string.Concat(text.Split('\n').Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l + "\n"));
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(table)));
        comments.ShouldContain(c => c.StartsWith("# table-sha256: " + sha, StringComparison.Ordinal));
        response.Content.Headers.ContentDisposition.FileName!.ShouldContain(sha[..8]);
    }

    [Fact]
    public async Task Every_kind_carries_the_evidence_block_and_the_same_request_twice_gives_the_same_bytes()
    {
        await OnAsync();
        foreach (var (kind, query) in Kinds)
        {
            Factory.Services.GetRequiredService<ExportSlot>().Reset();
            var first = await CsvAsync(kind, query);
            var second = await CsvAsync(kind, query);

            second.ShouldBe(first, kind);
            first.ShouldStartWith("# xDrishti research export; table " + kind);
            first.ShouldContain("# basis: gross");
            first.ShouldContain("# population rule:");
            first.ShouldContain("# as-of: ");
            first.ShouldContain("# folds: ");
            first.ShouldContain("# limit: ");
            first.ShouldContain("# table-rows: ");
            first.ShouldContain("# table-sha256: ");
            first.ShouldEndWith("\n# end of export, rows " + (Parse(first).Rows.Count - 1).ToString(CultureInfo.InvariantCulture) + "\n");
            first.ShouldNotContain("\r");
            first.ShouldNotStartWith("﻿");
        }
    }

    [Fact]
    public async Task The_trades_file_has_one_row_for_each_trade_the_report_counts_and_pages_are_not_visible_in_it()
    {
        await OnAsync();
        var (comments, rows) = Parse(await CsvAsync("report-trades", new { runs = Both, limit = "5", cursor = "o3.deadbeef" }));

        rows.Count.ShouldBe(1 + 93, "the limit and the cursor of the screen are ignored");
        rows[0].ShouldBe(["run_key", "trade_key", "strategy", "timeframe", "side", "symbol", "baskets", "entry_date", "exit_date", "r", "sessions_held", "style", "exit", "provisional", "ordinal"]);
        rows.Skip(1).Sum(r => double.Parse(r[9], CultureInfo.InvariantCulture)).ShouldBe(11, 1e-9);
        comments.ShouldContain("# table-rows: 93");
    }

    [Fact]
    public async Task A_group_below_the_minimum_has_a_state_word_and_no_digit_where_there_is_no_value()
    {
        await OnAsync();
        var (_, rows) = Parse(await CsvAsync("report-groups", new { runs = Both, lens = "instrument,exit" }));
        var header = rows[0];
        var checkedStates = new HashSet<string>();
        foreach (var row in rows.Skip(1))
        {
            for (var i = 0; i + 2 < header.Count; i++)
            {
                if (!header[i + 1].EndsWith("_state", StringComparison.Ordinal))
                {
                    continue;
                }

                checkedStates.Add(row[i + 1]);
                if (row[i + 1] is "value" or "provisional")
                {
                    double.TryParse(row[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _).ShouldBeTrue(header[i]);
                }
                else
                {
                    row[i].ShouldBe(string.Empty, header[i] + " of a non-value");
                    row[i + 2].ShouldNotBeEmpty("a non-value says why");
                }
            }
        }

        checkedStates.ShouldContain("value");
    }

    [Fact]
    public async Task A_net_file_says_net_and_what_the_owner_confirmed_and_a_gross_one_says_gross()
    {
        await OnAsync();
        var run = StoredRunFixtures.Run(costed: true, to: RunStoreRoundTripTests.NextTo(), strategy: "RPTNET", fixtureEdges: false);
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(FixtureSheet.Sheet, "test", DateTimeOffset.UnixEpoch, Ct);
            (await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
            (await scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>().HandleAsync(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor), Ct)).IsSuccess.ShouldBeTrue();
        }

        await ExecuteSqlAsync($"INSERT INTO workflow.charge_sheet_events (kind, actor, reason) VALUES ('gate_on', 'owner:test@example.com', 'test'); INSERT INTO workflow.charge_sheet_events (kind, sheet_id, rates_sha256, actor, reason) VALUES ('confirm', 'fixture-a', '{FixtureSheet.Sheet.RatesSha256}', 'owner:test@example.com', 'test')");
        var net = await CsvAsync("report-trades", new { runs = run.RunKey, basis = "net" });
        var gross = await CsvAsync("report-trades", new { runs = run.RunKey });

        net.ShouldContain("# basis: net R after charges; charge sheet confirmed by the owner: charge sheet 'fixture-a' with rates " + FixtureSheet.Sheet.RatesSha256[..12]);
        gross.ShouldContain("# basis: gross R; before charges");
        net.ShouldNotBe(gross);
    }

    [Fact]
    public async Task A_table_over_the_row_cap_is_413_with_the_sentence_and_no_file_and_no_audit_row_and_a_lowered_cap_is_honoured_never_raised()
    {
        await OnAsync();
        var configuration = Factory.Services.GetRequiredService<IConfiguration>();
        try
        {
            configuration["Exports:MaxCsvRows"] = "10";
            using var refused = await PostAsync("report-trades");
            var body = await refused.Content.ReadFromJsonAsync<JsonElement>(Ct);

            refused.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
            body.GetProperty("code").GetString().ShouldBe("exports.too_large");
            body.GetProperty("title").GetString()!.ShouldContain("Narrow the filters");
            refused.Content.Headers.ContentDisposition.ShouldBeNull();
            (await EventsAsync()).ShouldBe(0);

            configuration["Exports:MaxCsvRows"] = "100";
            (await PostAsync("report-trades")).StatusCode.ShouldBe(HttpStatusCode.OK);
            configuration["Exports:MaxCsvRows"] = "999999999";
            (await PostAsync("report-groups")).StatusCode.ShouldBe(HttpStatusCode.OK);

            configuration["Exports:MaxCsvRows"] = null;
            configuration["Exports:MaxCsvBytes"] = "500";
            (await ProblemAsync("report-trades")).ShouldBe((HttpStatusCode.RequestEntityTooLarge, "exports.too_large"));
        }
        finally
        {
            configuration["Exports:MaxCsvRows"] = null;
            configuration["Exports:MaxCsvBytes"] = null;
        }
    }

    [Fact]
    public async Task The_seventh_export_in_a_minute_is_429_and_a_refusal_because_of_the_query_still_counts_as_a_start()
    {
        await OnAsync();
        for (var i = 0; i < ExportSlot.PerMinute; i++)
        {
            (await PostAsync("report-groups")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await ProblemAsync("report-groups")).ShouldBe((HttpStatusCode.TooManyRequests, "auth.rate_limited"));
        (await EventsAsync()).ShouldBe(ExportSlot.PerMinute);
    }

    [Fact]
    public async Task One_export_writes_one_audit_row_with_counts_only_and_no_log_event_has_a_place_for_a_value()
    {
        await OnAsync();
        var before = await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events");
        var text = await CsvAsync("report-trades", new { runs = Both, symbol = "NSE:RPT1", strategy = "RPTA" });

        (await EventsAsync()).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events")).ShouldBe(before + 1);
        var detail = await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'ReportExported'");
        detail.ShouldMatch(@"^kind=report-trades format=csv rows=32 bytes=\d+ filters=3$");
        detail.ShouldNotContain("RPT");
        detail.ShouldNotContain(ReportFixture.RunA.RunKey);
        (await ScalarAsync<string>("SELECT actor FROM accounts.account_events WHERE event = 'ReportExported'")).ShouldBe("owner:" + ApiFactory.OwnerEmail);
        text.ShouldContain("NSE:RPT1");

        // the log methods take the kind, the format, counts, milliseconds and a code: no parameter could carry a symbol, a run key or a figure
        var names = typeof(ExportLogEvents).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters()).Select(p => p.Name).Distinct().Order().ToList();
        names.ShouldBe(["bytes", "code", "format", "kind", "logger", "ms", "rows"]);
    }

    [Fact]
    public async Task No_file_holds_the_owner_the_session_or_a_secret()
    {
        await OnAsync();
        foreach (var (kind, query) in Kinds)
        {
            var text = await CsvAsync(kind, query);
            text.ShouldNotContain(ApiFactory.OwnerEmail, Case.Insensitive);
            text.ShouldNotContain(Factory.OwnerPassword);
            text.ShouldNotContain("owner:", Case.Insensitive);
            text.ShouldNotContain("token", Case.Insensitive);
            text.ShouldNotContain("secret", Case.Insensitive);
        }
    }

    [Fact]
    public async Task The_same_numbers_as_the_screen_for_the_what_works_ranking_the_pivot_and_the_equity_curve()
    {
        await OnAsync();
        var ww = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/reports/what-works?runs={Both}", UriKind.Relative), Ct);
        var (_, wwRows) = Parse(await CsvAsync("report-what-works", new { runs = Both }));
        wwRows.Count.ShouldBe(1 + ww.GetProperty("ranked").GetArrayLength() + ww.GetProperty("notRanked").GetArrayLength());
        wwRows.Skip(1).Count(r => r[1] == "ranked").ShouldBe(ww.GetProperty("ranked").GetArrayLength());

        var pivot = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/reports/pivot?runs={Both}&lens=basket,strategy&metric=total_r", UriKind.Relative), Ct);
        var (_, pivotRows) = Parse(await CsvAsync("report-pivot", new { runs = Both, lens = "basket,strategy", metric = "total_r" }));
        pivotRows.Count.ShouldBe(1 + pivot.GetProperty("cells").GetArrayLength());
        pivotRows[0].ShouldBe(["row", "column", "n", "total_r", "total_r_state", "total_r_reason"]);

        var equity = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/reports/equity?runs={Both}&lens=strategy", UriKind.Relative), Ct);
        var (_, eqRows) = Parse(await CsvAsync("report-equity", new { runs = Both, lens = "strategy" }));
        eqRows.Count.ShouldBe(1 + equity.GetProperty("series").EnumerateArray().Sum(s => Math.Max(1, s.GetProperty("points").GetArrayLength())));
    }
}
