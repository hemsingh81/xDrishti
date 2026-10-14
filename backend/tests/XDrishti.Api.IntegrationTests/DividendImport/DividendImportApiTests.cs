using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.BrokerHistory;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.DividendImport;
using static XDrishti.Api.IntegrationTests.Infrastructure.DividendImportApi;

namespace XDrishti.Api.IntegrationTests.DividendImport;

/// <summary>
/// The dividend file import over HTTP on the real database (P2-15, ADR 0028): preview, import with choices and remembered names, the figures that follow, undo, the same file again, the guards. The account holds EXMPL (30 shares at 100, the lot dated
/// Fri 9 Oct 2026; the master names it "EXMPL Limited"); the clock is Monday 2026-10-12 10:00 India time.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DividendImportApiTests(ApiFactory factory) : BrokerHistoryTestBase(factory)
{
    private const string Exmpl = "01 Oct 2026,EXMPL,10,30,300.00";

    private const string Nobody = "23 Sept 2026,Nobody Knows Ltd,2.5,40,100.00";

    [Fact]
    public async Task The_preview_reads_the_file_matches_the_holding_and_stores_nothing()
    {
        var held = await HeldAsync();

        var report = await Client.PreviewDividendsAsync(held.Account.Id, Csv(Exmpl, Nobody, Exmpl, "bad,EXMPL,1,1,1"), Ct);

        report.Format.ShouldBe("dividends");
        (report.File.Rows, report.File.Parsed, report.File.Repeated, report.File.Distinct, report.File.BadTotal, report.File.ToAdd, report.File.NeedsMatch).ShouldBe((4, 3, 1, 2, 1, 2, 1));
        var item = report.Items[0];
        (item.Line, item.PayDate, item.Name, item.PerShare, item.Quantity, item.Amount, item.Status).ShouldBe((2, new DateOnly(2026, 10, 1), "EXMPL", 10m, 30L, 300m, "new"));
        (item.Match.Kind, item.Match.HoldingId, item.Match.Symbol, item.Match.Isin).ShouldBe(("name", held.HoldingId, "EXMPL", TradesApi.Isin));
        report.Items[1].Status.ShouldBe("needs_match");
        (report.Totals.Count, report.Totals.Amount).ShouldBe((2, 400m));
        report.Totals.ByYear.ShouldHaveSingleItem().Year.ShouldBe(2026);
        report.BeforeFirstLot.ShouldBe(new DividendBeforeFirstLot(2, 400m, new DateOnly(2026, 10, 9)));
        report.Digest.Length.ShouldBe(64);
        (await CountAsync("SELECT count(*) FROM portfolio.dividends")).ShouldBe(0);
        (await CountAsync("SELECT count(*) FROM portfolio.dividend_batches")).ShouldBe(0);
        (await EventsAsync("DividendsImported")).ShouldBe(0);
    }

    [Fact]
    public async Task Importing_stores_the_batch_and_the_figures_count_the_dividends_exactly_like_typed_in_ones()
    {
        var held = await HeldAsync();
        var before = await Client.SummaryAsync(string.Empty, Ct);

        var result = await Client.ImportDividendsAsync(held.Account.Id, Csv(Exmpl, Nobody), Ct);

        (result.Imported, result.Amount, result.Allocated, result.InstrumentOnly, result.Unallocated, result.AlreadyImported).ShouldBe((2, 400m, 1, 0, 1, 0));
        result.Batch.ShouldNotBeNull().ShouldSatisfyAllConditions(
            b => b.Status.ShouldBe("imported"),
            b => b.AccountName.ShouldBe("Example One"),
            b => b.FileName.ShouldBe("dividend-payout.csv"),
            b => b.ActiveDividends.ShouldBe(2),
            b => b.CreatedBy.ShouldBe(ApiFactory.OwnerEmail));
        var row = await RowAsync();
        var summary = await Client.SummaryAsync(string.Empty, Ct);
        var income = row.Income.ShouldNotBeNull();
        (income.Dividends, income.Unallocated).ShouldBe((300m, null));
        (summary.Income!.Dividends, summary.Income.Unallocated).ShouldBe((400m, 100m));
        (summary with { Income = null }).ShouldBe(before with { Income = null });
        (await CountAsync("SELECT count(*) FROM portfolio.dividends WHERE source = 'csv' AND batch_id IS NOT NULL AND external_id IS NOT NULL")).ShouldBe(2);
    }

    [Fact]
    public async Task The_dividend_lists_show_the_source_the_quantity_the_batch_and_the_file_name_of_an_unallocated_one()
    {
        var held = await HeldAsync();
        var result = await Client.ImportDividendsAsync(held.Account.Id, Csv(Exmpl, Nobody), Ct);

        var page = await Client.AccountDividendsAsync(held.Account.Id, string.Empty, Ct);

        page.Items.Select(d => (d.Source, d.Quantity, d.BatchId, d.HoldingId, d.ScripName, d.PerShare, d.Amount)).ShouldBe(
        [
            ("csv", 30L, result.Batch!.Id, held.HoldingId, null, 10m, 300m),
            ("csv", 40L, result.Batch.Id, null, "Nobody Knows Ltd", 2.5m, 100m),
        ]);
        (await Client.HoldingDividendsAsync(held.HoldingId, string.Empty, Ct)).Items.ShouldHaveSingleItem().Source.ShouldBe("csv");
    }

    [Fact]
    public async Task The_same_file_again_stores_nothing_undo_takes_the_dividends_out_of_the_figures_and_importing_again_stores_them_again()
    {
        var held = await HeldAsync();
        var csv = Csv(Exmpl, Nobody);
        var first = await Client.ImportDividendsAsync(held.Account.Id, csv, Ct);

        var again = await Client.ImportDividendsAsync(held.Account.Id, csv, Ct);
        var undone = await Client.UndoDividendBatchAsync(first.Batch!.Id, Ct);
        var twice = await Client.UndoDividendBatchAsync(first.Batch.Id, Ct);
        var afterUndo = (await RowAsync()).Income;
        var summaryAfterUndo = (await Client.SummaryAsync(string.Empty, Ct)).Income;
        var third = await Client.ImportDividendsAsync(held.Account.Id, csv, Ct);

        (again.Imported, again.AlreadyImported, again.Batch).ShouldBe((0, 2, null));
        (undone.AlreadyUndone, undone.Voided, undone.Batch.Status, undone.Batch.ActiveDividends).ShouldBe((false, 2, "undone", 0));
        (twice.AlreadyUndone, twice.Voided).ShouldBe((true, 0));
        afterUndo.ShouldBeNull();
        summaryAfterUndo.ShouldBeNull();
        (third.Imported, third.AlreadyImported).ShouldBe((2, 0));
        (await RowAsync()).Income!.Dividends.ShouldBe(300m);
        (await CountAsync("SELECT count(*) FROM portfolio.dividends")).ShouldBe(4);
        (await CountAsync("SELECT count(*) FROM portfolio.dividends WHERE voided_at IS NULL")).ShouldBe(2);
        (await Client.DividendBatchesAsync($"?accountId={held.Account.Id}", Ct)).Items.Select(b => b.Status).ShouldBe(["imported", "undone"]);
    }

    [Fact]
    public async Task A_stale_digest_is_a_409_with_the_fresh_report_and_stores_nothing()
    {
        var held = await HeldAsync();
        var csv = Csv(Exmpl);
        var digest = (await Client.PreviewDividendsAsync(held.Account.Id, csv, Ct)).Digest;
        await Client.AddDividendAsync(new { holdingId = held.HoldingId, payDate = "2026-10-01", amount = 300 }, Ct);

        using var response = await Client.ImportDividendsRawAsync(held.Account.Id, csv, digest, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.ProblemAsync(Ct);
        problem.GetProperty("code").GetString().ShouldBe("portfolio.preview_stale");
        var report = problem.GetProperty("report");
        report.GetProperty("digest").GetString().ShouldNotBe(digest);
        report.GetProperty("items")[0].GetProperty("status").GetString().ShouldBe("possible_duplicate");
        (await CountAsync("SELECT count(*) FROM portfolio.dividends WHERE source = 'csv'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_possible_duplicate_of_a_typed_in_dividend_is_left_out_unless_its_line_is_ticked()
    {
        var held = await HeldAsync();
        await Client.AddDividendAsync(new { holdingId = held.HoldingId, payDate = "2026-10-01", amount = 300 }, Ct);
        var csv = Csv(Exmpl);

        var left = await Client.ImportDividendsAsync(held.Account.Id, csv, Ct);
        var ticked = await Client.ImportDividendsAsync(held.Account.Id, csv, Ct, includeDuplicates: [2]);

        (left.Imported, left.PossibleDuplicatesSkipped).ShouldBe((0, 1));
        (ticked.Imported, ticked.PossibleDuplicatesSkipped).ShouldBe((1, 0));
        (await RowAsync()).Income!.Dividends.ShouldBe(600m);
    }

    [Fact]
    public async Task A_choice_with_remember_saves_an_alias_that_the_next_preview_uses_and_the_audit_has_ids_and_counts_only()
    {
        var held = await HeldAsync();
        var csv = Csv("01 Oct 2026,Example Co Ltd,10,30,300.00");

        var result = await Client.ImportDividendsAsync(held.Account.Id, csv, Ct, choices: new[] { new { name = "Example Co Ltd", holdingId = held.HoldingId, remember = true } });
        var next = await Client.PreviewDividendsAsync(held.Account.Id, Csv("02 Oct 2026,EXAMPLE CO,10,30,300.00"), Ct);

        (result.Allocated, result.AliasesSaved).ShouldBe((1, 1));
        (next.Items.Single().Match.Kind, next.Items.Single().Match.HoldingId, next.Items.Single().Status).ShouldBe(("alias", held.HoldingId, "new"));
        (await CountAsync("SELECT count(*) FROM portfolio.dividend_aliases WHERE name_key = 'example co' AND holding_id IS NOT NULL")).ShouldBe(1);
        (await EventsAsync("DividendAliasSaved")).ShouldBe(1);
        (await EventsAsync("DividendsImported")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM accounts.account_events WHERE detail LIKE '%Example Co%' OR detail LIKE '%dividend-payout%' OR detail LIKE '%300%'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_choice_naming_nothing_in_the_file_or_a_holding_of_nobody_is_a_400_on_choices_and_a_broken_choices_field_too()
    {
        var held = await HeldAsync();
        var csv = Csv(Exmpl);
        var digest = (await Client.PreviewDividendsAsync(held.Account.Id, csv, Ct)).Digest;

        using var unknownName = await Client.ImportDividendsRawAsync(held.Account.Id, csv, digest, Ct, choices: new[] { new { name = "Not Here", none = true } });
        using var foreign = await Client.ImportDividendsRawAsync(held.Account.Id, csv, digest, Ct, choices: new[] { new { name = "EXMPL", holdingId = Guid.NewGuid() } });
        using var broken = await Client.ImportDividendsRawAsync(held.Account.Id, csv, digest, Ct, choicesText: "{not json");

        foreach (var response in new[] { unknownName, foreign, broken })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["choices"]);
        }

        (await CountAsync("SELECT count(*) FROM portfolio.dividends")).ShouldBe(0);
    }

    [Fact]
    public async Task A_tradebook_dropped_on_the_dividend_import_and_a_dividend_list_dropped_on_the_trade_import_are_answered_as_the_wrong_kind_of_file()
    {
        var held = await HeldAsync();

        using var tradebook = await Client.PreviewDividendsRawAsync(held.Account.Id, TradesApi.Csv(TradesApi.BaseRows), Ct);
        using var sideless = await Client.PreviewDividendsRawAsync(held.Account.Id, "Trade Date,Symbol,Order Type,Segment,Quantity,Price\n2026-10-05,EXMPL,Market,EQ,10,90\n", Ct);
        using var onTrades = await Client.PreviewRawAsync(held.Account.Id, Csv(Exmpl), Ct);
        using var tradesSideless = await Client.PreviewRawAsync(held.Account.Id, "Trade Date,Symbol,Order Type,Segment,Quantity,Price\n2026-10-05,EXMPL,Market,EQ,10,90\n", Ct);
        using var unknown = await Client.PreviewDividendsRawAsync(held.Account.Id, "foo,bar\n1,2\n", Ct);

        foreach (var (response, detected) in new[] { (tradebook, "trades"), (sideless, "tradebook_without_sides"), (onTrades, "dividends"), (tradesSideless, "tradebook_without_sides") })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = await response.ProblemAsync(Ct);
            (problem.GetProperty("code").GetString(), problem.GetProperty("detected").GetString()).ShouldBe(("portfolio.wrong_file_kind", detected));
        }

        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.ProblemAsync(Ct)).GetProperty("errors").GetProperty("file")[0].GetString()!.ShouldContain("Dividend Paid");
    }

    [Fact]
    public async Task The_digest_the_file_and_the_account_are_required_and_the_account_must_exist()
    {
        var held = await HeldAsync();
        var csv = Csv(Exmpl);

        using var noDigest = await Client.ImportDividendsRawAsync(held.Account.Id, csv, null, Ct);
        using var noFile = await Client.PostAsync(new Uri("/api/portfolio/dividends/preview", UriKind.Relative), new MultipartFormDataContent { { new StringContent(held.Account.Id.ToString()), "accountId" } }, Ct);
        using var noAccount = await Client.PostAsync(new Uri("/api/portfolio/dividends/preview", UriKind.Relative), new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "a.csv" } }, Ct);
        using var notMultipart = await Client.PostAsync(new Uri("/api/portfolio/dividends/preview", UriKind.Relative), new StringContent("{}", Encoding.UTF8, "application/json"), Ct);
        using var unknown = await Client.PreviewDividendsRawAsync(Guid.NewGuid(), csv, Ct);
        using var emptyFile = await Client.PreviewDividendsRawAsync(held.Account.Id, string.Empty, Ct);

        (await noDigest.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["digest"]);
        (await noFile.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["file"]);
        (await noAccount.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["accountId"]);
        notMultipart.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await emptyFile.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["file"]);
    }

    [Fact]
    public async Task An_account_without_the_portfolio_role_is_a_409_and_an_unknown_batch_is_a_404()
    {
        await HeldAsync();
        var plain = await UnconnectedAccountAsync("Plain", "1000000002", portfolioRole: false);

        using var roleOff = await Client.PreviewDividendsRawAsync(plain.Id, Csv(Exmpl), Ct);
        using var missing = await Client.PostAsync($"/api/portfolio/dividend-batches/{Guid.NewGuid()}/undo", Ct);

        roleOff.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await roleOff.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.role_off");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_history_is_paged_newest_first_and_refuses_bad_paging()
    {
        var held = await HeldAsync();
        await Client.ImportDividendsAsync(held.Account.Id, Csv(Exmpl), Ct);
        var second = await Client.ImportDividendsAsync(held.Account.Id, Csv("02 Oct 2026,EXMPL,10,30,300.00"), Ct);

        var page = await Client.DividendBatchesAsync("?pageSize=1", Ct);
        using var bad = await Client.GetAsync("/api/portfolio/dividend-batches?pageSize=0", Ct);

        page.Total.ShouldBe(2);
        page.Items.ShouldHaveSingleItem().Id.ShouldBe(second.Batch!.Id);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_import_and_an_undo_drop_the_cached_histories_a_preview_does_not()
    {
        var held = await HeldAsync();
        var cache = Factory.Services.GetRequiredService<IPortfolioHistoryCache>();
        var csv = Csv(Exmpl);

        var before = cache.Version;
        await Client.PreviewDividendsAsync(held.Account.Id, csv, Ct);
        var afterPreview = cache.Version;
        var imported = await Client.ImportDividendsAsync(held.Account.Id, csv, Ct);
        var afterImport = cache.Version;
        await Client.UndoDividendBatchAsync(imported.Batch!.Id, Ct);
        var afterUndo = cache.Version;

        afterPreview.ShouldBe(before);
        afterImport.ShouldBeGreaterThan(afterPreview);
        afterUndo.ShouldBeGreaterThan(afterImport);
    }

    [Fact]
    public async Task A_preview_of_20000_rows_answers_within_five_seconds()
    {
        var held = await HeldAsync();
        var rows = Enumerable.Range(0, 20_000).Select(k => string.Create(
            CultureInfo.InvariantCulture, $"{new DateOnly(1995, 1, 2).AddDays(k / 500 * 180):dd MMM yyyy},{(k % 2 == 0 ? "EXMPL" : "Company Number " + (k % 500))},,,{(k + 1) / 100m:0.00}")).ToArray();

        var clock = Stopwatch.StartNew();
        var report = await Client.PreviewDividendsAsync(held.Account.Id, Csv(rows), Ct);
        clock.Stop();

        report.File.Rows.ShouldBe(20_000);
        report.Items.Count.ShouldBe(20_000);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }
}
