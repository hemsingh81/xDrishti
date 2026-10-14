using System.Diagnostics;
using System.Globalization;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.DividendImport;
using static XDrishti.Application.Tests.Portfolio.DividendImport.DividendFiles;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>
/// The whole flow on a made-up file with the shape of the owner's (P2-15 criteria 3, 6 and 11, "Done when"): 254 rows of 106 names, 71 of them holdings and 10 more in the instrument master; preview, import, the same file again, undo, import again.
/// The expected figures come from the model that wrote the file (<see cref="SampleShape"/>), not from reading it.
/// </summary>
public sealed class DividendSampleShapeTests
{
    private static readonly SampleShape Shape = new();

    private static async Task<(DividendScenario Scenario, Dictionary<int, HoldingData> Held)> StartAsync()
    {
        var s = await DividendScenario.StartAsync();
        var held = new Dictionary<int, HoldingData>();
        for (var name = 0; name < SampleShape.Held; name++)
        {
            held[name] = s.Hold(SampleShape.HoldingName(name), SampleShape.Symbol(name), acquired: new DateOnly(2025, 1, 1));
        }

        for (var name = SampleShape.Held; name < SampleShape.Held + SampleShape.InMaster; name++)
        {
            s.Names.Shares.Add(SampleShape.Share(name));
        }

        return (s, held);
    }

    [Fact]
    public async Task The_model_has_the_shape_of_the_file_it_stands_for()
    {
        await Task.CompletedTask;

        (Shape.Rows.Count, Shape.Distinct.Count).ShouldBe((254, 253));
        Shape.Rows.Select(r => r.Name).Distinct().Count().ShouldBe(SampleShape.Names);
        (Shape.Rows.Count(r => r.Date.Year == 2024), Shape.Rows.Count(r => r.Date.Year == 2025), Shape.Rows.Count(r => r.Date.Year == 2026)).ShouldBe((62, 100, 92));
        Shape.Rows.ShouldAllBe(r => r.Date <= Today);
        Shape.Csv().ShouldContain("Sept");
        Shape.Csv().ShouldContain("\"1,");
    }

    [Fact]
    public async Task The_reader_gives_254_rows_one_identical_repeat_the_total_and_per_share_times_quantity_equals_paid_on_every_row()
    {
        var file = (await ReadAsync(Shape.Csv())).Value;

        (file.Rows, file.Parsed, file.Items.Count, file.Repeated, file.BadTotal).ShouldBe((254, 254, 253, 1, 0));
        file.ParsedAmount.ShouldBe(Shape.TotalPaid);
        file.Items.Sum(i => i.Amount).ShouldBe(Shape.DistinctPaid);
        file.Items.ShouldAllBe(i => i.PerShare * i.Quantity == i.Amount);
        file.Repeats.ShouldHaveSingleItem().Occurrences.ShouldBe(2);
        file.Items.Select(i => i.NameKey).Distinct().Count().ShouldBe(SampleShape.Names);
    }

    [Fact]
    public async Task The_preview_matches_the_71_holdings_the_10_master_names_and_leaves_the_rest_for_the_owner()
    {
        var (s, held) = await StartAsync();

        var report = await s.PreviewOkAsync(Shape.Csv());

        report.Items.Count.ShouldBe(253);
        (report.File.Rows, report.File.Parsed, report.File.Repeated, report.File.Distinct, report.File.BadTotal, report.File.ParsedAmount).ShouldBe((254, 254, 1, 253, 0, Shape.TotalPaid));
        (report.Totals.Count, report.Totals.Amount).ShouldBe((253, Shape.DistinctPaid));
        var byYear = report.Totals.ByYear.Select(y => (y.Year, y.Count)).ToList();
        byYear.ShouldBe([(2024, 62), (2025, 100), (2026, 91)]);
        report.BeforeFirstLot.ShouldBe(new DividendBeforeFirstLot(Shape.Before2025, Shape.Distinct.Where(p => p.Date.Year < 2025).Sum(p => p.Paid), new DateOnly(2025, 1, 1)));
        report.Items.Count(i => i.Match.HoldingId is not null).ShouldBe(Shape.DistinctOf(SampleShape.IsHeld));
        report.Items.Count(i => i.Match.Kind == "master").ShouldBe(Shape.DistinctOf(SampleShape.IsInMaster));
        report.Items.Count(i => i.Status == "needs_match").ShouldBe(Shape.DistinctOf(n => !SampleShape.IsHeld(n) && !SampleShape.IsInMaster(n)));
        report.Items.Count(i => i.Status == "ambiguous").ShouldBe(0);
        report.Items.Where(i => i.Match.HoldingId is not null).ShouldAllBe(i => i.Match.Kind == "name");
        report.Items.Count(i => i.Warnings.Contains("amount_mismatch")).ShouldBe(0);
        held.Count.ShouldBe(SampleShape.Held);
    }

    [Fact]
    public async Task Every_distinct_row_is_matched_to_the_holding_its_model_name_stands_for()
    {
        var (s, held) = await StartAsync();
        var report = await s.PreviewOkAsync(Shape.Csv());
        var byLine = report.Items.ToDictionary(i => i.Line);
        var lines = Shape.Csv().Split('\n');

        // The model's row i is on line i + 2 up to the repeated row, which is the 202nd data row; the repeat takes line 203.
        for (var i = 0; i < Shape.Distinct.Count; i++)
        {
            var payment = Shape.Distinct[i];
            var line = i <= 200 ? i + 2 : i + 3;
            lines[line - 1].ShouldContain(payment.Text);
            var item = byLine[line];
            if (SampleShape.IsHeld(payment.Name))
            {
                item.Match.HoldingId.ShouldBe(held[payment.Name].Id, $"line {line}");
            }
        }
    }

    [Fact]
    public async Task Importing_the_file_stores_253_dividends_the_same_file_again_stores_nothing_and_after_an_undo_it_stores_them_again()
    {
        var (s, _) = await StartAsync();
        var csv = Shape.Csv();

        var first = await s.ImportOkAsync(csv);
        var again = await s.ImportOkAsync(csv);
        Ok(await s.UndoAsync(first.Batch!.Id));
        var afterUndo = await s.ImportOkAsync(csv);

        (first.Imported, first.Amount, first.AlreadyImported).ShouldBe((253, Shape.DistinctPaid, 0));
        (first.Allocated, first.InstrumentOnly, first.Unallocated).ShouldBe(
            (Shape.DistinctOf(SampleShape.IsHeld), Shape.DistinctOf(SampleShape.IsInMaster), Shape.DistinctOf(n => !SampleShape.IsHeld(n) && !SampleShape.IsInMaster(n))));
        (again.Imported, again.AlreadyImported, again.Batch).ShouldBe((0, 253, null));
        (afterUndo.Imported, afterUndo.AlreadyImported, afterUndo.Amount).ShouldBe((253, 0, Shape.DistinctPaid));
        s.Active().Count.ShouldBe(253);
        s.Active().Sum(d => d.Amount).ShouldBe(Shape.DistinctPaid);
        s.Active().Count(d => d.Quantity is null).ShouldBe(0);
        s.Active().Count(d => d.HoldingId is null && d.ScripName is null).ShouldBe(0);
    }

    [Fact]
    public async Task Two_payments_of_one_name_on_one_day_are_two_dividends_of_the_right_amounts()
    {
        var (s, held) = await StartAsync();
        await s.ImportOkAsync(Shape.Csv());
        var name = Shape.Distinct[70].Name;
        var day = Shape.Distinct[70].Date;

        var sameDay = s.Active().Where(d => d.HoldingId == held[name].Id && d.PayDate == day).Select(d => (d.PerShare, d.Amount)).OrderBy(x => x.Amount).ToList();

        sameDay.ShouldBe([(4.5m, 450m), (5m, 500m)]);
    }

    [Fact]
    public async Task A_preview_of_20000_rows_against_500_holdings_and_a_master_of_3000_shares_takes_a_few_seconds_at_most_with_one_read_of_each_kind()
    {
        var s = await DividendScenario.StartAsync();
        for (var i = 0; i < 500; i++)
        {
            s.Hold(Company(i).ToUpperInvariant() + " LIMITED", "SYM" + i.ToString(CultureInfo.InvariantCulture), acquired: new DateOnly(1995, 1, 1));
        }

        for (var i = 400; i < 3400; i++)
        {
            var name = i < 1700 ? Company(i) : "Filler " + i.ToString(CultureInfo.InvariantCulture) + " Works";
            s.Names.Shares.Add(DividendHoldings.Share(name.ToUpperInvariant() + " LTD", "MS" + i.ToString(CultureInfo.InvariantCulture), "INE" + i.ToString("D6", CultureInfo.InvariantCulture) + "A01"));
        }

        var rows = Enumerable.Range(0, 20_000).Select(k => Row(new DateOnly(1995, 1, 2).AddDays(k / 1500 * 180), Company(k % 1500) + (k % 3 == 0 ? " Ltd" : string.Empty), 2, 10 + (k % 7), 2m * (10 + (k % 7)))).ToArray();
        var csv = Csv(rows);

        var clock = Stopwatch.StartNew();
        var report = await s.PreviewOkAsync(csv);
        clock.Stop();

        report.File.Rows.ShouldBe(20_000);
        report.Items.Count.ShouldBe(20_000);
        report.Items.Count(i => i.Match.Kind == "name").ShouldBeGreaterThan(0);
        report.Items.Count(i => i.Match.Kind == "master").ShouldBeGreaterThan(0);
        (s.Holdings.Reads, s.Names.Reads, s.Fx.Dividends.IdentityReads).ShouldBe((1, 1, 1));
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    /// <summary>A made-up company name that is unique for every number.</summary>
    private static string Company(int n)
    {
        string[] syllables = ["ka", "lo", "mi", "ra", "tu", "ve", "no", "sa", "di", "pe", "zu", "ha"];
        return $"{char.ToUpperInvariant(syllables[n % 12][0])}{syllables[n % 12][1..]}{syllables[n / 12 % 12]}{syllables[n / 144 % 12]} Works";
    }
}
