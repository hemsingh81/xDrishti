using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Application.Tests.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.DividendImport.DividendFiles;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>The dividend preview (ADR 0028, P2-15 criterion 8): it reads, plans and changes nothing. Clock: 2026-10-15, India.</summary>
public sealed class DividendPreviewTests
{
    [Fact]
    public async Task The_preview_says_what_would_be_added_and_stores_and_records_nothing()
    {
        var s = await DividendScenario.StartAsync();
        s.Hold("Orion Steel Limited", "ORION");
        var saves = s.Fx.Data.SaveCount;

        var report = await s.PreviewOkAsync(Csv("13 Jun 2024,Orion Steel,4,25,100.00", "23 Sept 2024,Nobody Knows,2.5,40,100.00"));

        report.Format.ShouldBe("dividends");
        (report.Totals.Count, report.Totals.Amount).ShouldBe((2, 200m));
        s.Fx.Dividends.Dividends.ShouldBeEmpty();
        s.Batches.Batches.ShouldBeEmpty();
        s.Aliases.Aliases.ShouldBeEmpty();
        s.Fx.Accounts.Events.Where(e => e.Event is AccountEventType.DividendsImported or AccountEventType.DividendAliasSaved).ShouldBeEmpty();
        s.Fx.Data.SaveCount.ShouldBe(saves);
    }

    [Fact]
    public async Task Each_row_carries_its_status_match_and_figures_and_the_file_summary_its_counts()
    {
        var s = await DividendScenario.StartAsync();
        var orion = s.Hold("Orion Steel Limited", "ORION", "INE000A01011");

        var report = await s.PreviewOkAsync(Csv("13 Jun 2024,Orion Steel,4,25,100.00", "23 Sept 2024,Nobody Knows,2.5,40,100.00", "bad date,Orion Steel,1,1,1.00", "13 Jun 2024,Orion Steel,4,25,100.00"));

        var first = report.Items[0];
        (first.Line, first.PayDate, first.Name, first.PerShare, first.Quantity, first.Amount, first.Status).ShouldBe((2, new DateOnly(2024, 6, 13), "Orion Steel", 4m, 25L, 100m, "new"));
        (first.Match.Kind, first.Match.HoldingId, first.Match.Symbol, first.Match.Isin, first.Match.Name).ShouldBe(("name", orion.Id, "ORION", "INE000A01011", "Orion Steel Limited"));
        (report.Items[1].Status, report.Items[1].Match.Kind).ShouldBe(("needs_match", "none"));
        var file = report.File;
        (file.FileName, file.Rows, file.Parsed, file.Repeated, file.Distinct, file.BadTotal, file.ToAdd, file.NeedsMatch).ShouldBe(("dividends.csv", 4, 3, 1, 2, 1, 2, 1));
        file.Bad.ShouldHaveSingleItem().Line.ShouldBe(4);
        file.IdenticalRepeats.ShouldHaveSingleItem().Lines.ShouldBe([2, 5]);
        file.ParsedAmount.ShouldBe(300m);
    }

    [Fact]
    public async Task An_ambiguous_name_lists_the_holdings_that_fit()
    {
        var s = await DividendScenario.StartAsync();
        s.Hold("Delta Power Limited", "DELTA");
        s.Hold("DELTA POWER LTD", "DELTA-B");

        var report = await s.PreviewOkAsync(Csv("13 Jun 2024,Delta Power,4,25,100.00"));

        var item = report.Items.Single();
        (item.Status, item.Match.Kind, item.Match.HoldingId).ShouldBe(("ambiguous", "name", null));
        item.Match.Candidates.Select(c => c.Symbol).ShouldBe(["DELTA", "DELTA-B"]);
        report.File.Ambiguous.ShouldBe(1);
    }

    [Fact]
    public async Task The_name_is_plain_text_exactly_as_cleaned_and_the_amount_mismatch_and_pre_first_lot_dates_are_flagged()
    {
        var s = await DividendScenario.StartAsync();
        s.Hold("Orion Steel Limited", "ORION");

        var report = await s.PreviewOkAsync(Csv("13 Jun 2024,\"Orion <script>x</script> Steel\",2.2,100,230.00", "13 Jun 2025,Orion Steel,2,10,20.00"));

        report.Items[0].Name.ShouldBe("Orion <script>x</script> Steel");
        report.Items[0].Warnings.ShouldBe(["amount_mismatch", "before_first_lot"]);
        report.Items[1].Warnings.ShouldBeEmpty();
        report.BeforeFirstLot.ShouldBe(new DividendBeforeFirstLot(1, 230m, new DateOnly(2025, 1, 1)));
    }

    [Fact]
    public async Task A_file_already_imported_is_all_imported_and_nothing_is_to_add()
    {
        var s = await DividendScenario.StartAsync();
        s.Hold("Orion Steel Limited", "ORION");
        var csv = Csv("13 Jun 2024,Orion Steel,4,25,100.00");
        await s.ImportOkAsync(csv);

        var report = await s.PreviewOkAsync(csv);

        report.Items.Single().Status.ShouldBe("imported");
        (report.File.AlreadyImported, report.File.ToAdd, report.Totals.Count, report.Totals.Amount).ShouldBe((1, 0, 0, 0m));
    }

    [Fact]
    public async Task A_row_equal_to_a_typed_in_dividend_is_a_possible_duplicate()
    {
        var s = await DividendScenario.StartAsync();
        var orion = s.Hold("Orion Steel Limited", "ORION");
        s.AddManual(orion.Id, new DateOnly(2024, 6, 13), 100m);

        var report = await s.PreviewOkAsync(Csv("13 Jun 2024,Orion Steel,4,25,100.00"));

        (report.Items.Single().Status, report.File.PossibleDuplicates, report.Totals.Count).ShouldBe(("possible_duplicate", 1, 0));
    }

    [Fact]
    public async Task A_tradebook_is_answered_as_a_wrong_kind_of_file_and_an_unknown_file_names_the_accepted_headers()
    {
        var s = await DividendScenario.StartAsync();

        var trades = await s.PreviewAsync(CsvWithHeader("date,symbol,isin,side,quantity,price", "2024-03-05,EXMPL,,buy,10,90"));
        var unknown = await s.PreviewAsync(CsvWithHeader("foo,bar", "1,2"));

        (trades.Error.Code, trades.Error.Kind, trades.Error.Payload!["detected"]).ShouldBe((PortfolioErrors.WrongFileKindCode, XDrishti.Domain.Common.ErrorKind.Validation, "trades"));
        unknown.Error.Code.ShouldBe(PortfolioErrors.InvalidCode);
        FieldsOf(unknown).ShouldBe(["file"]);
        Text(unknown.Error).ShouldContain("Dividend Paid");
    }

    [Fact]
    public async Task A_file_over_10_MB_and_an_unknown_or_role_less_account_are_refused()
    {
        var s = await DividendScenario.StartAsync();
        var big = new string('a', (10 * 1024 * 1024) + 1);
        var off = await s.Fx.Connections.Accounts.CreatedAsync(Inputs.Account(name: "Plain", clientId: "1000000002"));

        var tooBig = await s.PreviewAsync(big);
        var unknown = await s.PreviewAsync(Csv(), Guid.NewGuid());
        var roleOff = await s.PreviewAsync(Csv(), off.Id);

        FieldsOf(tooBig).ShouldBe(["file"]);
        unknown.Error.Code.ShouldBe(PortfolioErrors.NotFoundCode);
        roleOff.Error.Code.ShouldBe(PortfolioErrors.RoleOffCode);
    }

    [Fact]
    public async Task The_reads_are_one_per_kind_whatever_the_number_of_rows()
    {
        var s = await DividendScenario.StartAsync();
        s.Hold("Orion Steel Limited", "ORION");
        var rows = Enumerable.Range(0, 300).Select(i => Row(new DateOnly(2020, 1, 1).AddDays(i), i % 2 == 0 ? "Orion Steel" : "Unknown " + i, 1, 10, 10)).ToArray();

        await s.PreviewOkAsync(Csv(rows));

        (s.Holdings.Reads, s.Names.Reads, s.Fx.Dividends.IdentityReads).ShouldBe((1, 1, 1));
    }
}
