using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Serilog.Core;
using XDrishti.Api.CostSheets;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Simulate;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.CostSheets;

/// <summary>
/// The store and the three verbs of P3-03f against a real TimescaleDB (criteria 4, 7, 8 and 10): the sheet is the invented <c>fixture-a</c> (the numbers of the TEST sheet under an id that is not <c>test-</c>), the verbs run through the real host and
/// <c>CostSheetCommand.RunAsync</c> with the container's connection string, and the report files are invented, with sentinel values planted where a bill number, a name, a client code, a mobile number or an e-mail address would be.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class CostSheetDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Key = "research.cost_sheet.fixture-a";

    private static readonly string[] Sentinels = ["ZZ-SENTINEL-NAME", "ZZ-SENTINEL-MOBILE", "ZZ-SENTINEL-EMAIL", "ZZBILL", "ZZ SCRIP", "1000000001", "10000.00", "20000.00"];

    private sealed record Outcome(int Exit, string Out, string Error, IReadOnlyCollection<string> Logs);

    private async Task<Outcome> VerbAsync(string[] args, Action<IServiceCollection>? configure = null, CancellationToken? token = null)
    {
        var logs = new CapturedLogs();
        var overrides = new Dictionary<string, string?> { ["Database:ConnectionString"] = Factory.AdminConnectionString, ["BrokerVault:Key"] = string.Empty };
        using var host = SimulateHost.Create(overrides, services =>
        {
            services.AddSingleton<ILogEventSink>(logs);
            configure?.Invoke(services);
        }, "xd-cost-sheet");
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await CostSheetCommand.RunAsync(args, host.Services, new SimulateConsole(output, error), token ?? Ct);

        return new Outcome(exit, output.ToString(), error.ToString(), [.. logs.All]);
    }

    private static string WriteFile(TempFolder temp, string name, string text)
    {
        var path = Path.Combine(temp.Path, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private async Task StoreAsync(CostSheet sheet, string actor = "cli:cost-sheet")
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(sheet, actor, DateTimeOffset.UnixEpoch.AddDays(20_000), Ct);
        (await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
    }

    private async Task<long> RowsAsync(string key = Key) => await ScalarAsync<long>("SELECT count(*) FROM platform.app_settings WHERE key = @key", ("key", key));

    private async Task<Dictionary<string, long>> CountsAsync()
    {
        await using var connection = await OpenAsync();
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand("SELECT table_schema || '.' || table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema') AND table_schema NOT LIKE '%timescaledb%' ORDER BY 1", connection))
        await using (var reader = await list.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var counts = new Dictionary<string, long>();
        foreach (var table in tables)
        {
#pragma warning disable CA2100 // the names come from the catalog query above
            await using var count = new NpgsqlCommand("SELECT count(*) FROM " + table, connection);
#pragma warning restore CA2100
            counts[table] = (long)(await count.ExecuteScalarAsync(Ct))!;
        }

        return counts;
    }

    // criterion 4: the store

    [Fact]
    public async Task A_sheet_saved_through_the_store_is_found_again_exactly_under_the_key_with_its_actor_and_instant()
    {
        await StoreAsync(FixtureSheet.Sheet);

        using var scope = Factory.Services.CreateScope();
        var found = await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().FindAsync(FixtureSheet.Id, Ct);

        found.IsSuccess.ShouldBeTrue();
        found.Value.ShouldBe(FixtureSheet.Sheet);
        found.Value.ToCanonicalJson().ShouldBe(FixtureSheet.Json);
        found.Value.RatesSha256.ShouldBe(FixtureSheet.Sheet.RatesSha256);
        (await ScalarAsync<string>("SELECT value FROM platform.app_settings WHERE key = @key", ("key", Key))).ShouldBe(FixtureSheet.Json);
        (await ScalarAsync<string>("SELECT updated_by FROM platform.app_settings WHERE key = @key", ("key", Key))).ShouldBe("cli:cost-sheet");
        (await ScalarAsync<DateTime>("SELECT updated_at FROM platform.app_settings WHERE key = @key", ("key", Key))).ToUniversalTime().ShouldBe(DateTimeOffset.UnixEpoch.AddDays(20_000).UtcDateTime);
    }

    [Fact]
    public async Task A_second_save_of_the_same_id_overwrites_the_row()
    {
        await StoreAsync(FixtureSheet.Sheet);
        var noted = FixtureSheet.Build(FixtureSheet.Id, "check 2026-10-15: ok");

        await StoreAsync(noted, "cli:other");

        (await RowsAsync()).ShouldBe(1);
        (await ScalarAsync<string>("SELECT updated_by FROM platform.app_settings WHERE key = @key", ("key", Key))).ShouldBe("cli:other");
        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().FindAsync(FixtureSheet.Id, Ct)).Value.Checked.ShouldBe("check 2026-10-15: ok");
    }

    [Fact]
    public async Task A_test_sheet_is_refused_by_save_and_never_found_even_if_a_row_was_written_by_hand()
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICostSheetStore>();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('research.cost_sheet.test-synthetic-1', @value, 'hand', now())", ("value", TestCostSheet.Json));
        }

        await Should.ThrowAsync<ArgumentException>(async () => await store.SaveAsync(TestCostSheet.Sheet, "x", DateTimeOffset.UnixEpoch, Ct));
        var found = await store.FindAsync(TestCostSheet.Id, Ct);

        found.Error.Code.ShouldBe(CostErrors.SheetNotFoundCode);
    }

    [Fact]
    public async Task A_row_added_meanwhile_makes_the_second_save_report_false_and_changes_nothing_of_the_first()
    {
        using var first = Factory.Services.CreateScope();
        using var second = Factory.Services.CreateScope();
        var firstStore = first.ServiceProvider.GetRequiredService<ICostSheetStore>();
        var secondStore = second.ServiceProvider.GetRequiredService<ICostSheetStore>();
        await firstStore.SaveAsync(FixtureSheet.Sheet, "cli:first", DateTimeOffset.UnixEpoch, Ct);
        await secondStore.SaveAsync(FixtureSheet.Build(FixtureSheet.Id, "second"), "cli:second", DateTimeOffset.UnixEpoch, Ct);

        (await first.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
        (await second.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeFalse();

        (await ScalarAsync<string>("SELECT updated_by FROM platform.app_settings WHERE key = @key", ("key", Key))).ShouldBe("cli:first");
    }

    [Fact]
    public async Task A_stored_text_that_no_longer_parses_or_names_another_sheet_is_costs_sheet_invalid()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES (@key, '{\"id\":\"fixture-a\"}', 'hand', now())", ("key", Key));
            await ExecuteAsync(connection, "INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('research.cost_sheet.fixture-b', @value, 'hand', now())", ("value", FixtureSheet.Json));
        }

        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICostSheetStore>();

        (await store.FindAsync(FixtureSheet.Id, Ct)).Error.Code.ShouldBe(CostErrors.SheetInvalidCode);
        (await store.FindAsync("fixture-b", Ct)).Error.Code.ShouldBe(CostErrors.SheetInvalidCode);
        (await store.FindAsync("Fixture-A", Ct)).Error.Code.ShouldBe(CostErrors.SheetNotFoundCode, "an id that differs only in case is not the same id");
    }

    // criterion 7: load

    [Fact]
    public async Task Load_stores_a_valid_file_prints_the_id_the_hash_the_label_and_unchecked_and_nothing_of_the_rates()
    {
        using var temp = new TempFolder();
        var path = WriteFile(temp, "sheet.json", FixtureSheet.Json);

        var outcome = await VerbAsync(["load", path]);

        outcome.Exit.ShouldBe(0, outcome.Error);
        outcome.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldBe(["sheet: fixture-a", "status: created", "rates hash: " + FixtureSheet.Sheet.RatesSha256, "label: FIXTURE sheet (invented numbers)", "unchecked"]);
        (await ScalarAsync<string>("SELECT updated_by FROM platform.app_settings WHERE key = @key", ("key", Key))).ShouldBe("cli:cost-sheet");
        foreach (var rate in new[] { "brokerage_pct", "stt_sell_pct", "0.035", "0.09", "gst_pct" })
        {
            outcome.Out.ShouldNotContain(rate);
            outcome.Error.ShouldNotContain(rate);
            outcome.Logs.ShouldAllBe(l => !l.Contains(rate, StringComparison.Ordinal));
        }

        var line = outcome.Logs.Single(l => l.Contains("loaded", StringComparison.Ordinal));
        line.ShouldContain("fixture-a");
        line.ShouldContain(FixtureSheet.Sheet.RatesSha256);
    }

    [Fact]
    public async Task A_file_with_three_problems_prints_them_as_path_code_message_exits_1_and_stores_nothing()
    {
        using var temp = new TempFolder();
        var broken = FixtureSheet.Json.Replace("\"gst_pct\":18", "\"gst_pct\":-1", StringComparison.Ordinal).Replace("\"source\":\"invented for tests; not a broker's sheet\",", string.Empty, StringComparison.Ordinal).Replace("\"rounding\":\"half_away_from_zero\"", "\"rounding\":\"sideways\"", StringComparison.Ordinal);

        var outcome = await VerbAsync(["load", WriteFile(temp, "broken.json", broken)]);

        outcome.Exit.ShouldBe(1);
        var lines = outcome.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBeInRange(3, 20);
        lines.ShouldAllBe(l => l.Split(": ").Length >= 3, outcome.Error);
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_test_id_a_repeated_property_name_unreadable_text_and_a_missing_file_exit_1_and_store_nothing()
    {
        using var temp = new TempFolder();
        var duplicate = FixtureSheet.Json.Replace("\"id\":\"fixture-a\"", "\"id\":\"fixture-a\",\"id\":\"fixture-b\"", StringComparison.Ordinal);

        var test = await VerbAsync(["load", WriteFile(temp, "test.json", TestCostSheet.Json)]);
        var repeated = await VerbAsync(["load", WriteFile(temp, "dup.json", duplicate)]);
        var garbage = await VerbAsync(["load", WriteFile(temp, "garbage.json", "this is not json")]);
        var missing = await VerbAsync(["load", Path.Combine(temp.Path, "nowhere.json")]);

        test.Exit.ShouldBe(1);
        test.Error.ShouldContain(CostErrors.TestSheetNotStoredCode);
        repeated.Exit.ShouldBe(1);
        repeated.Error.ShouldContain(CostErrors.DuplicateProperty);
        garbage.Exit.ShouldBe(1);
        missing.Exit.ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM platform.app_settings")).ShouldBe(0);
    }

    [Fact]
    public async Task A_file_over_64_KB_is_refused_before_it_is_read_and_one_of_exactly_64_KB_goes_to_the_parser()
    {
        using var temp = new TempFolder();
        var over = Path.Combine(temp.Path, "over.json");
        await File.WriteAllBytesAsync(over, new byte[CostSheetCommand.MaxFileBytes + 1], Ct);
        var exact = Path.Combine(temp.Path, "exact.json");
        await File.WriteAllTextAsync(exact, FixtureSheet.Json.PadRight(CostSheetCommand.MaxFileBytes), Ct);

        var refused = await VerbAsync(["load", over]);
        var parsed = await VerbAsync(["load", exact]);

        refused.Exit.ShouldBe(1);
        refused.Error.ShouldContain("It was not read");
        parsed.Exit.ShouldBe(1);
        parsed.Error.ShouldContain(CostErrors.SheetTooLongCode, Case.Insensitive);
        parsed.Error.ShouldNotContain("It was not read");
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Other_rates_under_an_existing_id_exit_1_naming_both_hashes_without_replace_and_succeed_with_it()
    {
        using var temp = new TempFolder();
        await StoreAsync(FixtureSheet.Sheet);
        var other = FixtureSheet.Json.Replace("\"stt_sell_pct\":0.09", "\"stt_sell_pct\":0.095", StringComparison.Ordinal);
        var otherHash = CostSheetJson.Parse(other).Value.RatesSha256;
        var path = WriteFile(temp, "other.json", other);

        var refused = await VerbAsync(["load", path]);
        (await ScalarAsync<string>("SELECT value FROM platform.app_settings WHERE key = @key", ("key", Key))).ShouldBe(FixtureSheet.Json);
        var replaced = await VerbAsync(["load", path, "--replace"]);

        refused.Exit.ShouldBe(1);
        refused.Error.ShouldContain(FixtureSheet.Sheet.RatesSha256);
        refused.Error.ShouldContain(otherHash);
        replaced.Exit.ShouldBe(0, replaced.Error);
        replaced.Out.ShouldContain("status: replaced");
        (await ScalarAsync<string>("SELECT value FROM platform.app_settings WHERE key = @key", ("key", Key))).ShouldBe(CostSheetJson.Parse(other).Value.ToCanonicalJson());
    }

    [Fact]
    public async Task The_same_rates_with_a_checked_note_update_the_description_without_replace_keep_the_hash_and_a_second_load_changes_nothing()
    {
        using var temp = new TempFolder();
        await StoreAsync(FixtureSheet.Sheet);
        var noted = WriteFile(temp, "noted.json", FixtureSheet.Build(FixtureSheet.Id, "check 2026-10-15: ok").ToCanonicalJson());

        var first = await VerbAsync(["load", noted]);
        var at = await ScalarAsync<DateTime>("SELECT updated_at FROM platform.app_settings WHERE key = @key", ("key", Key));
        var second = await VerbAsync(["load", noted]);

        first.Exit.ShouldBe(0, first.Error);
        first.Out.ShouldContain("status: description updated (same rates)");
        first.Out.ShouldContain("rates hash: " + FixtureSheet.Sheet.RatesSha256);
        first.Out.ShouldContain("checked");
        first.Out.ShouldNotContain("unchecked");
        second.Out.ShouldContain("status: unchanged");
        (await ScalarAsync<DateTime>("SELECT updated_at FROM platform.app_settings WHERE key = @key", ("key", Key))).ShouldBe(at);
    }

    [Fact]
    public async Task A_concurrent_write_exits_3_and_stores_nothing()
    {
        using var temp = new TempFolder();
        var path = WriteFile(temp, "sheet.json", FixtureSheet.Json);

        var outcome = await VerbAsync(["load", path], services => services.AddScoped<IPlatformUnitOfWork, RefusingUnit>());

        outcome.Exit.ShouldBe(3);
        outcome.Error.ShouldContain(CostErrors.ConcurrentWriteCode);
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_database_that_cannot_be_reached_exits_3_with_one_short_line()
    {
        using var temp = new TempFolder();
        var path = WriteFile(temp, "sheet.json", FixtureSheet.Json);
        var logs = new CapturedLogs();
        using var host = SimulateHost.Create(new Dictionary<string, string?> { ["Database:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=none;Username=nobody;Timeout=2;Pooling=false" }, services => services.AddSingleton<ILogEventSink>(logs), "xd-cost-sheet");
        var error = new StringWriter();

        var exit = await CostSheetCommand.RunAsync(["load", path], host.Services, new SimulateConsole(new StringWriter(), error), Ct);

        exit.ShouldBe(3);
        error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(1);
        error.ToString().Length.ShouldBeLessThan(400);
    }

    // criterion 8: show

    [Fact]
    public async Task Show_prints_the_canonical_text_the_hash_and_the_label_and_an_unknown_id_exits_1()
    {
        await StoreAsync(FixtureSheet.Sheet);

        var shown = await VerbAsync(["show", FixtureSheet.Id]);
        var unknown = await VerbAsync(["show", "nope"]);
        var test = await VerbAsync(["show", TestCostSheet.Id]);

        shown.Exit.ShouldBe(0, shown.Error);
        shown.Out.ShouldBe(FixtureSheet.Json + "\nrates hash: " + FixtureSheet.Sheet.RatesSha256 + "\nlabel: costs: sheet fixture-a (not checked against bills)\n");
        unknown.Exit.ShouldBe(1);
        unknown.Error.ShouldContain(CostErrors.SheetNotFoundCode);
        test.Exit.ShouldBe(1);
        shown.Logs.Count(l => l.Contains("shown", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task A_wrong_command_line_is_exit_2_with_the_usage_text_and_touches_nothing()
    {
        foreach (var args in new[] { Array.Empty<string>(), ["load"], ["load", "a", "b"], ["show"], ["check", "x"], ["show", "x", "--replace"], ["load", "a", "--bogus"], ["nope"] })
        {
            var outcome = await VerbAsync(args);

            outcome.Exit.ShouldBe(2, string.Join(' ', args));
            outcome.Error.ShouldContain("usage:");
            outcome.Out.ShouldBeEmpty();
        }
    }

    // criterion 10: check

    private static string Report(params string[] rows) =>
        string.Join('\n',
        [
            "Global transction report,From 01-04-2026 to 15-10-2026", "Name,ZZ-SENTINEL-NAME", "UCC,1000000001", "Mobile,ZZ-SENTINEL-MOBILE", "Email ID,ZZ-SENTINEL-EMAIL@example.invalid", string.Empty,
            "Date,Scrip Name,Exchange,Bill No.,Buy Qty.,Buy Value,Sell Qty.,Sell Value,Brokerage,GST,STT,SEBI Fees,Stamp Duty,Txn. Charges,Oth. Charges,Gross Amount",
            .. rows, string.Empty, "Net P&L,0.00,Brokerage,0.00,Gross P&L,0.00,Total Charges,0.00", string.Empty, "NOTE : invented",
        ]) + "\n";

    private static string Row(string date, string name, string bill, long buyQty, string buy, long sellQty, string sell, params string[] charges) =>
        string.Join(',', new[] { date, name, "NSE", bill, buyQty.ToString(CultureInfo.InvariantCulture), buy, sellQty.ToString(CultureInfo.InvariantCulture), sell }.Concat(charges).Append("0.00").Select(c => "\"" + c + "\""));

    private static readonly string[] BuyCells = ["0.00", "0.08", "8.00", "0.05", "3.00", "0.40", "0.00"];

    [Fact]
    public async Task Check_reports_classes_counts_and_implied_rates_writes_nothing_and_prints_no_identifier_anywhere()
    {
        using var temp = new TempFolder();
        await StoreAsync(FixtureSheet.Sheet);
        var file = WriteFile(temp, "report.csv", Report(
            Row("07 Apr 2026 00:00:00", "ZZ SCRIP ONE", "ZZBILL01", 100, "10000.00", 0, "0.00", BuyCells),
            Row("08 Apr 2026 00:00:00", "ZZ SCRIP ONE", "ZZBILL02", 100, "10000.00", 0, "0.00", "0.00", "0.08", "8.00", "0.05", "3.00", "0.44", "0.00"),
            Row("09 Apr 2026 00:00:00", "ZZ SCRIP ONE", "ZZBILL03", 100, "10000.00", 0, "0.00", "0.00", "0.08", "8.00", "0.05", "3.00", "0.46", "0.00"),
            Row("10 Apr 2026 00:00:00", "ZZ SCRIP TWO", "ZZBILL04", 0, "0.00", 200, "20000.00", "0.00", "0.16", "18.00", "0.10", "0.00", "0.80", "0.00"),
            Row("13 Apr 2026 00:00:00", "ZZ SCRIP THREE", "ZZBILL05", 100, "10000.00", 100, "10500.00", "6.15", "1.31", "3.68", "0.10", "2.00", "1.03", "0.00"),
            Row("14 Apr 2026 00:00:00", "ZZ SCRIP ONE", "ZZBILL06", 100, "10000.00", 0, "0.00", "0.00", "0.08", "8.00", "0.05", "3.00", "0.40", "-0.01"),
            Row("15 Apr 2026 00:00:00", "FUT ZZ SCRIP FOUR 28APR2026", "ZZBILL07", 50, "5000.00", 0, "0.00", "20.00", "0.00", "0.00", "0.00", "0.00", "0.00", "0.00"),
            Row("16 Apr 2026 00:00:00", "ZZ SCRIP FIVE", "ZZBILL08", 100, "10000.00", 0, "0.00", "5.00", "0.90", "8.00", "0.05", "3.00", "0.40", "0.00")));
        var before = await CountsAsync();

        var outcome = await VerbAsync(["check", FixtureSheet.Id, file]);

        outcome.Exit.ShouldBe(0, outcome.Error);
        outcome.Out.ShouldContain("delivery equity: 4 rows, 3 within 0.05, 1 beyond; largest total difference 0.06");
        outcome.Out.ShouldContain("intraday equity: 1 rows, 1 within 0.05, 0 beyond; largest total difference 0.00");
        outcome.Out.ShouldContain("adjustments (a negative Oth. cell");
        outcome.Out.ShouldContain("skipped (not NSE cash equity): derivatives 1");
        outcome.Out.ShouldContain("unclassified (fit no class, not compared): 1");
        outcome.Out.ShouldContain("txn charges: min 0.004, median 0.0042, max 0.0046 % over 4 rows");
        outcome.Out.ShouldContain("suggested checked note: none (rows are beyond the tolerance)");
        outcome.Out.ShouldContain("what this cannot do");
        (await CountsAsync()).ShouldBe(before, "the check writes nothing");
        foreach (var sentinel in Sentinels)
        {
            outcome.Out.ShouldNotContain(sentinel);
            outcome.Error.ShouldNotContain(sentinel);
            outcome.Logs.ShouldAllBe(l => !l.Contains(sentinel, StringComparison.Ordinal), sentinel);
        }

        outcome.Logs.Single(l => l.Contains("checked", StringComparison.Ordinal) && l.Contains("fixture-a", StringComparison.Ordinal)).ShouldContain("5 rows compared, 1 beyond");
    }

    [Fact]
    public async Task Check_of_clean_rows_suggests_the_checked_note_and_loading_it_makes_the_label_stop_saying_unchecked()
    {
        using var temp = new TempFolder();
        await StoreAsync(FixtureSheet.Sheet);
        var file = WriteFile(temp, "report.csv", Report(
            Row("07 Apr 2026 00:00:00", "ZZ SCRIP ONE", "ZZBILL01", 100, "10000.00", 0, "0.00", BuyCells),
            Row("13 Apr 2026 00:00:00", "ZZ SCRIP THREE", "ZZBILL05", 100, "10000.00", 100, "10500.00", "6.15", "1.31", "3.68", "0.10", "2.00", "1.03", "0.00")));

        var checkedRun = await VerbAsync(["check", FixtureSheet.Id, file]);

        checkedRun.Exit.ShouldBe(0, checkedRun.Error);
        var note = checkedRun.Out.Split('\n').Single(l => l.StartsWith("suggested checked note (", StringComparison.Ordinal));
        var suggestion = note[(note.LastIndexOf("): ", StringComparison.Ordinal) + 3)..];
        suggestion.ShouldMatch(@"^check \d{4}-\d{2}-\d{2}: 1 intraday rows within 0.05, 1 delivery rows within 0.05$");
        var noted = WriteFile(temp, "noted.json", FixtureSheet.Build(FixtureSheet.Id, suggestion).ToCanonicalJson());
        (await VerbAsync(["load", noted])).Exit.ShouldBe(0);
        var shown = await VerbAsync(["show", FixtureSheet.Id]);
        shown.Out.ShouldContain("label: costs: sheet fixture-a\n");
        shown.Out.ShouldNotContain("not checked against bills");
    }

    [Fact]
    public async Task Check_refuses_an_unknown_sheet_and_an_unreadable_report_with_exit_1_naming_nothing_of_the_file()
    {
        using var temp = new TempFolder();
        await StoreAsync(FixtureSheet.Sheet);
        var bad = WriteFile(temp, "bad.csv", "not,a,report\n1,2,3\n");
        var good = WriteFile(temp, "good.csv", Report(Row("07 Apr 2026 00:00:00", "ZZ SCRIP ONE", "ZZBILL01", 100, "10000.00", 0, "0.00", BuyCells)));

        var unknown = await VerbAsync(["check", "nope", good]);
        var unreadable = await VerbAsync(["check", FixtureSheet.Id, bad]);
        var missing = await VerbAsync(["check", FixtureSheet.Id, Path.Combine(temp.Path, "missing.csv")]);

        unknown.Exit.ShouldBe(1);
        unreadable.Exit.ShouldBe(1);
        unreadable.Error.ShouldContain("File 1:");
        missing.Exit.ShouldBe(1);
    }

    private sealed class RefusingUnit : IPlatformUnitOfWork
    {
        public ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
}
