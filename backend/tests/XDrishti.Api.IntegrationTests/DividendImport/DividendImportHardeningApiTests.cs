using System.Diagnostics;
using System.Globalization;
using System.Net;
using XDrishti.Api.IntegrationTests.BrokerHistory;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Portfolio.DividendImport;
using static XDrishti.Api.IntegrationTests.Infrastructure.DividendImportApi;

namespace XDrishti.Api.IntegrationTests.DividendImport;

/// <summary>
/// The dividend file import's guards over HTTP on the real database (P2-15, ADR 0028; review round): a tradebook or a statement with an amount column is never read as dividends, two spellings of one company share a nameKey and one choice,
/// a very large import, parallel undos, the limits of the form. The account holds EXMPL (30 shares at 100; the master names it "EXMPL Limited"); the clock is Monday 2026-10-12 10:00 India time.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DividendImportHardeningApiTests(ApiFactory factory) : BrokerHistoryTestBase(factory)
{
    private const string Exmpl = "01 Oct 2026,EXMPL,10,30,300.00";

    private const string KestrelA = "01 Oct 2026,Kestrel Foods & Beverages,2,50,100.00";

    private const string KestrelB = "02 Oct 2026,Kestrel Foods and Beverages Ltd,2,50,100.00";

    public static TheoryData<string, string> TradebookHeaders => new()
    {
        { "Date,Symbol,Side,Quantity,Price,Net Amount", "trades" },
        { "Date,Symbol,Side,Quantity,Price,Amount", "trades" },
        { "Date,Symbol,Quantity,Price,Net Amount", "tradebook_without_sides" },
        { "Date,Symbol,Qty,Rate,Amount", "tradebook_without_sides" },
        { "Pay Date,Company,Amount", "statement" },
        { "Date,Symbol,Net Amount", "statement" },
    };

    [Theory]
    [MemberData(nameof(TradebookHeaders))]
    public async Task A_tradebook_or_a_statement_with_an_amount_column_is_a_400_wrong_file_kind_on_the_preview_and_the_import_and_stores_nothing(string header, string detected)
    {
        var held = await HeldAsync();
        var csv = header + "\n2026-10-05,EXMPL,buy,10,90,900\n2026-10-06,EXMPL,10,90,900\n";

        using var preview = await Client.PreviewDividendsRawAsync(held.Account.Id, csv, Ct);
        using var import = await Client.ImportDividendsRawAsync(held.Account.Id, csv, "0000", Ct);

        foreach (var response in new[] { preview, import })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = await response.ProblemAsync(Ct);
            (problem.GetProperty("code").GetString(), problem.GetProperty("detected").GetString()).ShouldBe(("portfolio.wrong_file_kind", detected));
        }

        (await CountAsync("SELECT count(*) FROM portfolio.dividends")).ShouldBe(0);
    }

    [Fact]
    public async Task The_real_dividend_payout_header_shape_and_a_plain_amount_beside_a_dividend_per_share_column_still_import()
    {
        var held = await HeldAsync();

        var real = await Client.ImportDividendsAsync(held.Account.Id, Csv(Exmpl), Ct);
        var plain = await Client.ImportDividendsAsync(held.Account.Id, "Pay Date,Company,Dividend Per Share,Quantity,Net Amount\n02 Oct 2026,EXMPL,10,30,300.00\n", Ct);

        (real.Imported, plain.Imported).ShouldBe((1, 1));
    }

    [Fact]
    public async Task Every_item_carries_the_name_key_and_two_spellings_of_one_company_have_the_same_one()
    {
        var held = await HeldAsync();

        var report = await Client.PreviewDividendsAsync(held.Account.Id, Csv(KestrelA, KestrelB, Exmpl), Ct);

        report.Items.Select(i => i.NameKey).ShouldBe(["kestrel foods and beverages", "kestrel foods and beverages", "exmpl"]);
        report.File.NeedsMatch.ShouldBe(2);
    }

    [Theory]
    [InlineData("Kestrel Foods & Beverages", "Kestrel Foods and Beverages Ltd")]
    [InlineData("kestrel foods and beverages", "Kestrel Foods and Beverages Ltd")]
    [InlineData("kestrel foods and beverages", "kestrel foods and beverages")]
    public async Task Choices_for_two_spellings_of_one_company_that_agree_are_one_choice_remembered_once(string first, string second)
    {
        var held = await HeldAsync();
        var csv = Csv(KestrelA, KestrelB);

        var result = await Client.ImportDividendsAsync(
            held.Account.Id, csv, Ct, choices: new[] { new { name = first, holdingId = held.HoldingId, remember = true }, new { name = second, holdingId = held.HoldingId, remember = true } });

        (result.Imported, result.Allocated, result.AliasesSaved).ShouldBe((2, 2, 1));
        (await CountAsync("SELECT count(*) FROM portfolio.dividend_aliases WHERE name_key = 'kestrel foods and beverages'")).ShouldBe(1);
        (await EventsAsync("DividendAliasSaved")).ShouldBe(1);
    }

    [Fact]
    public async Task Choices_for_two_spellings_of_one_company_that_disagree_are_a_400_on_choices_naming_the_key_and_store_nothing()
    {
        var held = await HeldAsync();
        var csv = Csv(KestrelA, KestrelB);
        var digest = (await Client.PreviewDividendsAsync(held.Account.Id, csv, Ct)).Digest;

        using var response = await Client.ImportDividendsRawAsync(
            held.Account.Id, csv, digest, Ct, choices: new object[] { new { name = "Kestrel Foods & Beverages", holdingId = held.HoldingId }, new { name = "Kestrel Foods and Beverages Ltd", none = true } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = await response.ReadFieldErrorsAsync(Ct);
        errors.Keys.ShouldBe(["choices"]);
        errors["choices"].Single().ShouldContain("kestrel foods and beverages");
        (await CountAsync("SELECT count(*) FROM portfolio.dividends")).ShouldBe(0);
    }

    [Fact]
    public async Task The_success_answer_of_an_import_has_no_report_but_the_stale_409_still_has_one()
    {
        var held = await HeldAsync();
        var csv = Csv(Exmpl);
        var digest = (await Client.PreviewDividendsAsync(held.Account.Id, csv, Ct)).Digest;

        using var stale = await Client.ImportDividendsRawAsync(held.Account.Id, csv, "stale", Ct);
        using var done = await Client.ImportDividendsRawAsync(held.Account.Id, csv, digest, Ct);

        done.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await done.ProblemAsync(Ct)).TryGetProperty("report", out _).ShouldBeFalse();
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ProblemAsync(Ct)).GetProperty("report").GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task An_import_of_20000_rows_is_stored_all_or_nothing_within_the_time_budget_and_undone_in_one_statement()
    {
        var held = await HeldAsync();
        var rows = Enumerable.Range(0, 20_000).Select(k => string.Create(
            CultureInfo.InvariantCulture, $"{new DateOnly(1995, 1, 2).AddDays(k / 500 * 180):dd MMM yyyy},{(k % 2 == 0 ? "EXMPL" : "Company Number " + (k % 500))},,,{(k + 1) / 100m:0.00}")).ToArray();
        var csv = Csv(rows);
        var digest = (await Client.PreviewDividendsAsync(held.Account.Id, csv, Ct)).Digest;

        var importClock = Stopwatch.StartNew();
        using var response = await Client.ImportDividendsRawAsync(held.Account.Id, csv, digest, Ct);
        importClock.Stop();
        var result = await response.ReadAsync<DividendImportResult>(Ct);

        (result.Imported, result.Allocated, result.Unallocated, result.Amount).ShouldBe((20_000, 10_000, 10_000, 20_000m * 20_001m / 2m / 100m));
        result.Batch.ShouldNotBeNull().ActiveDividends.ShouldBe(20_000);
        (await CountAsync("SELECT count(*) FROM portfolio.dividends WHERE source = 'csv' AND batch_id IS NOT NULL")).ShouldBe(20_000);
        (await CountAsync("SELECT count(DISTINCT external_id) FROM portfolio.dividends")).ShouldBe(20_000);
        (await EventsAsync("DividendsImported")).ShouldBe(1);
        importClock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30), $"the import took {importClock.Elapsed.TotalSeconds:0.0} s");

        var againClock = Stopwatch.StartNew();
        var again = await Client.ImportDividendsAsync(held.Account.Id, csv, Ct);
        againClock.Stop();
        var undoClock = Stopwatch.StartNew();
        var undone = await Client.UndoDividendBatchAsync(result.Batch.Id, Ct);
        undoClock.Stop();

        (again.Imported, again.AlreadyImported, again.Batch).ShouldBe((0, 20_000, null));
        (undone.Voided, undone.Batch.ActiveDividends).ShouldBe((20_000, 0));
        againClock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        undoClock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        Console.WriteLine($"[timing] import of 20,000 rows {importClock.Elapsed.TotalSeconds:0.0} s; same file again {againClock.Elapsed.TotalSeconds:0.0} s (preview + import); undo {undoClock.Elapsed.TotalSeconds:0.0} s");
    }

    [Fact]
    public async Task Two_undos_of_one_batch_at_once_void_and_audit_it_once()
    {
        var held = await HeldAsync();
        var imported = await Client.ImportDividendsAsync(held.Account.Id, Csv(Exmpl, Nobody), Ct);
        var batchId = imported.Batch!.Id;

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Client.UndoDividendBatchAsync(batchId, Ct)));

        results.Count(r => !r.AlreadyUndone).ShouldBe(1);
        results.Sum(r => r.Voided).ShouldBe(2);
        (await EventsAsync("DividendsUndone")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM portfolio.dividends WHERE voided_at IS NULL")).ShouldBe(0);
    }

    private const string Nobody = "23 Sept 2026,Nobody Knows Ltd,2.5,40,100.00";

    [Fact]
    public async Task The_full_set_of_2000_choices_fits_the_form_and_one_more_is_a_400_on_choices()
    {
        var held = await HeldAsync();
        var names = Enumerable.Range(0, 2_001).Select(k => string.Create(CultureInfo.InvariantCulture, $"Scrip{k:0000} {new string('x', 150)}")).ToArray();
        var csv = Csv([.. names.Select((n, k) => string.Create(CultureInfo.InvariantCulture, $"01 Oct 2026,{n},,,{k + 1}.00"))]);
        var digest = (await Client.PreviewDividendsAsync(held.Account.Id, csv, Ct)).Digest;

        using var tooMany = await Client.ImportDividendsRawAsync(held.Account.Id, csv, digest, Ct, choices: names.Select(n => new { name = n, none = true }).ToArray());
        using var full = await Client.ImportDividendsRawAsync(held.Account.Id, csv, digest, Ct, choices: names.Take(2_000).Select(n => new { name = n, none = true, remember = true }).ToArray());

        full.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await full.ReadAsync<DividendImportResult>(Ct)).AliasesSaved.ShouldBe(2_000);
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooMany.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["choices"]);
    }

    [Fact]
    public async Task A_text_field_over_the_limit_is_a_400_naming_choices_and_too_many_form_entries_are_a_400_naming_include_duplicates()
    {
        var held = await HeldAsync();
        var csv = Csv(Exmpl);
        var digest = (await Client.PreviewDividendsAsync(held.Account.Id, csv, Ct)).Digest;

        using var longField = await Client.ImportDividendsRawAsync(held.Account.Id, csv, digest, Ct, choicesText: new string(' ', 1_600_000));
        using var manyEntries = await ManyIncludeDuplicatesAsync(held.Account.Id, csv, digest, 20_100);
        using var manyLines = await Client.ImportDividendsRawAsync(held.Account.Id, csv, digest, Ct, includeDuplicates: Enumerable.Range(2, 20_001));

        foreach (var (response, field) in new[] { (longField, "choices"), (manyEntries, "includeDuplicates"), (manyLines, "includeDuplicates") })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, field);
            (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe([field]);
        }
    }

    [Fact]
    public async Task Include_duplicates_may_be_sent_once_per_line()
    {
        var held = await HeldAsync();
        await Client.AddDividendAsync(new { holdingId = held.HoldingId, payDate = "2026-10-01", amount = 300 }, Ct);
        var csv = Csv(Exmpl);
        var digest = (await Client.PreviewDividendsAsync(held.Account.Id, csv, Ct)).Digest;

        using var response = await ManyIncludeDuplicatesAsync(held.Account.Id, csv, digest, 3, firstLine: 2);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.ReadAsync<DividendImportResult>(Ct)).Imported.ShouldBe(1);
    }

    private async Task<HttpResponseMessage> ManyIncludeDuplicatesAsync(Guid accountId, string csv, string digest, int count, int firstLine = 1_000)
    {
        using var content = Upload(accountId, csv, digest);
        foreach (var line in Enumerable.Range(firstLine, count))
        {
            content.Add(new StringContent(line.ToString(CultureInfo.InvariantCulture)), "includeDuplicates");
        }

        return await Client.PostAsync(new Uri("/api/portfolio/dividends/import", UriKind.Relative), content, Ct);
    }
}
