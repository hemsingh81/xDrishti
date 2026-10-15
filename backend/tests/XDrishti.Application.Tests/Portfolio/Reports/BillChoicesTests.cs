using XDrishti.Application.Portfolio.Reports;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportScenario;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>
/// What a scrip name of the report means when nothing knows it (ADR 0029, P2-17 criteria 5 and 9): it is imported without an ISIN and the preview says <i>needs a match</i>; the owner chooses once - a holding, an instrument or an ISIN - and the choice can be remembered
/// as an alias or a learned name, so that later reports need no question. Nothing is guessed.
/// </summary>
public sealed class BillChoicesTests
{
    private const string Widgets = "UNKNOWN WIDGETS LTD";

    private static readonly string Row = GtrRow("07 Apr 2026 00:00:00", Widgets, "NSE", "5001", 17, "1700.00", 0, "0.00", brokerage: "1.00");

    private static Task<ReportScenario> StartAsync() => ReportScenario.StartAsync(s =>
    {
        s.Hold("ZENITHE", Zenith, 30, 12m);
        s.ListSampleCompanies();
    });

    [Fact]
    public async Task A_name_nothing_knows_is_imported_without_an_isin_under_the_name_as_its_symbol_and_flagged_as_needing_a_match()
    {
        var s = await StartAsync();
        var text = Gtr([Row]);

        var preview = await s.PreviewOkAsync(text);
        var done = await s.ImportOkAsync(text, rebuild: false);

        var name = preview.Bill!.Names.Single();
        (name.Status, name.Kind, name.Isin).ShouldBe(("needs_match", "none", null));
        preview.Bill.NeedsMatch.ShouldBe(1);
        preview.Bill.NeedsMatchTrades.ShouldBe(1);
        preview.Warnings.Single(w => w.Code == "needs_match").Count.ShouldBe(1);
        preview.Trades!.Unmatched.Securities.ShouldBe(1);
        var trade = s.Active().Single();
        (trade.Isin, trade.TradingSymbol, trade.Source).ShouldBe((null, "UNKNOWN WIDGETS LTD", TradeSource.Bill));
        done.Trades!.Imported.ShouldBe(1);
    }

    [Fact]
    public async Task A_choice_of_a_holding_gives_the_trades_its_isin_and_symbol_and_joins_them_to_the_holding()
    {
        var s = await StartAsync();
        var holding = s.Fx.HoldingOf("ZENITHE");
        var choice = new BillChoice(DividendKey(Widgets), HoldingId: holding.Id);

        var preview = await s.PreviewOkAsync(Gtr([Row]), [choice]);
        await s.ImportOkAsync(Gtr([Row]), rebuild: false, choices: [choice]);

        preview.Bill!.Names.Single().Status.ShouldBe("resolved");
        preview.Bill.Names.Single().Kind.ShouldBe("choice");
        preview.Bill.NeedsMatch.ShouldBe(0);
        var trade = s.Active().Single();
        (trade.Isin, trade.TradingSymbol).ShouldBe((Zenith, "ZENITHE"));
    }

    [Fact]
    public async Task A_choice_of_an_instrument_and_of_a_typed_isin_resolve_the_name_too()
    {
        var s = await StartAsync();
        var nova = s.Names.Shares.Single(x => x.Isin == Nova);

        var byInstrument = await s.PreviewOkAsync(Gtr([Row]), [new BillChoice(Widgets, InstrumentId: nova.Id)]);
        var byIsin = await s.PreviewOkAsync(Gtr([Row]), [new BillChoice(Widgets, Isin: "INE777X01077")]);

        byInstrument.Bill!.Names.Single().Isin.ShouldBe(Nova);
        byIsin.Bill!.Names.Single().Isin.ShouldBe("INE777X01077");
    }

    [Fact]
    public async Task A_remembered_choice_of_a_holding_is_saved_as_an_alias_and_later_reports_need_no_question()
    {
        var s = await StartAsync();
        var holding = s.Fx.HoldingOf("ZENITHE");

        var done = await s.ImportOkAsync(Gtr([Row]), rebuild: false, choices: [new BillChoice(Widgets, HoldingId: holding.Id, Remember: true)]);
        var later = await s.PreviewOkAsync(Gtr([GtrRow("08 Apr 2026 00:00:00", Widgets, "NSE", "5002", 3, "300.00", 0, "0.00")]));

        done.AliasesSaved.ShouldBe(1);
        s.Aliases.Aliases.Single().NameKey.ShouldBe(DividendKey(Widgets));
        s.Aliases.Aliases.Single().HoldingId.ShouldBe(holding.Id);
        later.Bill!.Names.Single().Kind.ShouldBe("alias");
        later.Bill.NeedsMatch.ShouldBe(0);
        s.Fx.Events(AccountEventType.DividendAliasSaved).Single().Detail!.ShouldContain("1 new");
    }

    [Fact]
    public async Task A_remembered_isin_is_saved_as_a_learned_name_and_resolves_later_reports()
    {
        var s = await StartAsync();

        await s.ImportOkAsync(Gtr([Row]), rebuild: false, choices: [new BillChoice(Widgets, Isin: "INE777X01077", Remember: true)]);
        var later = await s.PreviewOkAsync(Gtr([GtrRow("08 Apr 2026 00:00:00", Widgets, "NSE", "5002", 3, "300.00", 0, "0.00")]));

        var learned = s.Learned.Aliases.Single();
        (learned.Isin, learned.Source, learned.NameKey).ShouldBe(("INE777X01077", SecurityAliasSource.Choice, DividendKey(Widgets)));
        later.Bill!.Names.Single().Kind.ShouldBe("learned");
        s.Fx.Events(AccountEventType.SecurityNamesLearned).Single().Detail.ShouldBe("Security names learned from the owner's choice: 1 new");
    }

    [Fact]
    public async Task A_choice_that_is_not_remembered_is_not_saved()
    {
        var s = await StartAsync();

        var done = await s.ImportOkAsync(Gtr([Row]), rebuild: false, choices: [new BillChoice(Widgets, Isin: "INE777X01077")]);

        done.AliasesSaved.ShouldBe(0);
        s.Aliases.Aliases.ShouldBeEmpty();
        s.Learned.Aliases.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("nameNotInFile", "No row of the file")]
    [InlineData("noTarget", "exactly one")]
    [InlineData("twoTargets", "exactly one")]
    [InlineData("badHolding", "not one of the account's")]
    [InlineData("badInstrument", "not an NSE share")]
    [InlineData("badIsin", "not an ISIN")]
    public async Task A_choice_that_makes_no_sense_is_refused_with_the_choices_field(string problem, string text)
    {
        var s = await StartAsync();
        var holding = s.Fx.HoldingOf("ZENITHE");
        var choice = problem switch
        {
            "nameNotInFile" => new BillChoice("Other Name", HoldingId: holding.Id),
            "noTarget" => new BillChoice(Widgets),
            "twoTargets" => new BillChoice(Widgets, HoldingId: holding.Id, Isin: "INE777X01077"),
            "badHolding" => new BillChoice(Widgets, HoldingId: Guid.NewGuid()),
            "badInstrument" => new BillChoice(Widgets, InstrumentId: Guid.NewGuid()),
            _ => new BillChoice(Widgets, Isin: "NOPE"),
        };

        var preview = await s.PreviewOkAsync(Gtr([Row]), [choice]);

        preview.Error!.Field.ShouldBe("choices");
        preview.Error.Message.ShouldContain(text);
    }

    [Fact]
    public async Task Choices_for_one_name_that_disagree_are_refused_and_the_ones_that_agree_are_one_choice()
    {
        var s = await StartAsync();
        var holding = s.Fx.HoldingOf("ZENITHE");

        var agree = await s.PreviewOkAsync(Gtr([Row]), [new BillChoice(Widgets, HoldingId: holding.Id), new BillChoice(DividendKey(Widgets), HoldingId: holding.Id, Remember: true)]);
        var disagree = await s.PreviewOkAsync(Gtr([Row]), [new BillChoice(Widgets, HoldingId: holding.Id), new BillChoice(Widgets, Isin: "INE777X01077")]);

        agree.Error.ShouldBeNull();
        disagree.Error!.Message.ShouldContain("disagree");
    }

    [Fact]
    public async Task A_name_that_fits_two_securities_is_ambiguous_and_lists_the_candidates_until_the_owner_chooses()
    {
        var s = await ReportScenario.StartAsync(x =>
        {
            x.Master("Atlas Power Ltd", "ATLASP", "INE001AA00001");
            x.Master("Atlas Power Limited", "ATLASQ", "INE002BB00002");
        });
        var row = GtrRow("07 Apr 2026 00:00:00", "ATLAS POWER", "NSE", "6001", 5, "500.00", 0, "0.00");

        var preview = await s.PreviewOkAsync(Gtr([row]));

        var name = preview.Bill!.Names.Single();
        name.Status.ShouldBe("ambiguous");
        name.Candidates.Select(c => c.Symbol).ShouldBe(["ATLASP", "ATLASQ"]);
        name.Isin.ShouldBeNull();
        preview.Bill.NeedsMatch.ShouldBe(1);
    }

    [Fact]
    public async Task Names_learned_from_a_demat_statement_resolve_a_security_nothing_else_lists()
    {
        var s = await StartAsync();
        var demat = Demat([DematRow("DELISTED RAIL WORKS LTD", "INE888D01088", "10")]);
        await s.ImportOkAsync(demat);

        var preview = await s.PreviewOkAsync(Gtr([GtrRow("07 Apr 2026 00:00:00", "Delisted Rail Works Limited", "NSE", "7001", 5, "50.00", 0, "0.00")]));

        var name = preview.Bill!.Names.Single();
        (name.Status, name.Kind, name.Isin).ShouldBe(("resolved", "learned", "INE888D01088"));
    }

    private static string DividendKey(string name) => XDrishti.Application.Portfolio.DividendImport.DividendNames.Key(name);
}
