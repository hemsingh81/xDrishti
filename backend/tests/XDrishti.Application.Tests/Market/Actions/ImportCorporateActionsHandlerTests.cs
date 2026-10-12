using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Tests.Market.Actions;

/// <summary>Importing corporate actions from CSV text: preview, the real import, unknown symbols, duplicates, bad lines and the limits.</summary>
public sealed class ImportCorporateActionsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private const string Actor = "owner@example.invalid";

    private const string Header = "symbol,exDate,kind,numerator,denominator";

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryActionData _data = new();
    private readonly FakeListedInstruments _listed = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ImportCorporateActionsHandler Handler => new(_listed, _data, _data, _data, _data, _clock);

    private ValueTask<XDrishti.Domain.Common.Result<ImportCorporateActionsResult>> ImportAsync(string? text, bool preview = false) =>
        Handler.HandleAsync(new ImportCorporateActionsCommand(text, preview, Actor), Ct);

    private Guid Listed(string ticker)
    {
        var id = _listed.Add("NSE:" + ticker);
        _data.Instruments[id] = new ActionInstrument(id, "NSE:" + ticker, ticker + " Ltd");
        return id;
    }

    private static (bool, int, int, int, int, int) Counts(ImportCorporateActionsResult r) => (r.Preview, r.Lines, r.Importable, r.Imported, r.Duplicates, r.Invalid);

    private static string Csv(params string[] lines) => Header + "\n" + string.Join('\n', lines);

    [Fact]
    public async Task A_preview_counts_what_an_import_would_do_and_writes_nothing()
    {
        Listed("ABB");
        Listed("TCS");

        var result = await ImportAsync(Csv("ABB,2026-09-01,split,1,5", "NSE:TCS,2026-08-03,bonus,1,1", "NOPE,2026-08-03,split,1,2"), preview: true);

        Counts(result.Value).ShouldBe((true, 3, 2, 0, 0, 0));
        result.Value.UnknownSymbols.ShouldBe(["NSE:NOPE"]);
        _data.Actions.ShouldBeEmpty();
        _data.Events.ShouldBeEmpty();
        _data.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task The_import_applies_the_new_actions_with_source_csv_and_audits_counts_and_symbols_only()
    {
        var abb = Listed("ABB");
        Listed("TCS");

        var result = await ImportAsync(Csv("ABB,2026-09-01,split,1,5", "tcs,2026-08-03,Bonus,1,1,", "  ", "XYZ,2026-08-03,split,1,2"));

        Counts(result.Value).ShouldBe((false, 3, 2, 2, 0, 0));
        result.Value.UnknownSymbols.ShouldBe(["NSE:XYZ"]);
        _data.Actions.Count.ShouldBe(2);
        _data.Actions.ShouldAllBe(a => a.Status == CorporateActionStatus.Applied && a.Source == CorporateActionSource.Csv && a.DecidedBy == Actor);
        _data.Actions.First(a => a.InstrumentId == abb).Factor.ShouldBe(0.2m);
        var audit = _data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Actor, audit.Detail).ShouldBe((AccountEventType.CorporateActionsImported, Actor, "CSV import: 2 added, 0 duplicates skipped, 1 unknown symbols, 0 invalid lines; NSE:ABB, NSE:TCS"));
    }

    [Fact]
    public async Task Columns_may_come_in_any_order_and_case_and_fields_may_be_quoted()
    {
        Listed("M&M");

        var result = await ImportAsync("\"Denominator\",Numerator,KIND,ex_date,Symbol,note\n5,1,\"split\",2026-09-01,\"m&m\",\"five, for one\"\n");

        result.Value.Imported.ShouldBe(1);
        var action = _data.Actions.ShouldHaveSingleItem();
        (action.Ratio, action.Kind, action.Note).ShouldBe(("1:5", CorporateActionKind.Split, "five, for one"));
        _data.Events.Single().Detail!.ShouldNotContain("five");
    }

    [Fact]
    public async Task A_line_that_repeats_an_existing_action_of_any_status_or_an_earlier_line_is_a_duplicate()
    {
        var abb = Listed("ABB");
        var ignored = CorporateAction.Suggest(abb, new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5, null, Now);
        ignored.Ignore("owner", Now);
        _data.Actions.Add(ignored);

        var result = await ImportAsync(Csv("ABB,2026-09-01,split,1,5", "ABB,2026-09-01,split,1,10", "ABB,2026-07-01,split,1,2", "ABB,2026-07-01,split,1,2", "ABB,2026-09-01,bonus,1,1"));

        Counts(result.Value).ShouldBe((false, 5, 2, 2, 3, 0));
        _data.Actions.Count.ShouldBe(3);
        _data.Actions.First().Status.ShouldBe(CorporateActionStatus.Ignored);
    }

    [Fact]
    public async Task Unknown_symbols_are_listed_once_and_never_guessed_and_an_ambiguous_listing_counts_as_unknown()
    {
        Listed("ABB");
        _listed.Matches["NSE:TWIN"] = [Guid.NewGuid(), Guid.NewGuid()];

        var result = await ImportAsync(Csv("ABBB,2026-09-01,split,1,5", "ABBB,2026-08-01,split,1,5", "TWIN,2026-08-01,split,1,5", "ABB,2026-08-01,split,1,5"));

        result.Value.UnknownSymbols.ShouldBe(["NSE:ABBB", "NSE:TWIN"]);
        result.Value.Imported.ShouldBe(1);
    }

    [Fact]
    public async Task Bad_lines_are_reported_with_their_line_and_field_and_skipped_while_the_rest_is_imported()
    {
        Listed("ABB");

        var result = await ImportAsync(Csv("ABB,2026-09-01,split,1,5", "AB B,2026-09-01,split,1,5", "ABB,09/01/2026,split,1,5", "ABB,2026-09-02,dividend,1,5", "ABB,2026-09-03,split,x,5", "ABB,2026-09-04,split,3,3", "ABB,2026-09-05,split,1"));

        (result.Value.Lines, result.Value.Imported, result.Value.Invalid).ShouldBe((7, 1, 6));
        result.Value.Errors.ShouldBe(
        [
            new ImportLineError(3, "symbol: Use the symbol, for example RELIANCE or NSE:RELIANCE."),
            new ImportLineError(4, "exDate: Use the date as yyyy-MM-dd."),
            new ImportLineError(5, "kind: Use split or bonus."),
            new ImportLineError(6, "numerator: Use a whole number from 1 to 100000."),
            new ImportLineError(7, "denominator: A split 1:1 changes nothing."),
            new ImportLineError(8, "denominator: Use a whole number from 1 to 100000."),
        ]);
    }

    [Fact]
    public async Task Only_the_first_fifty_problems_are_listed_but_all_are_counted()
    {
        Listed("ABB");

        var result = await ImportAsync(Csv([.. Enumerable.Range(0, 60).Select(i => $"ABB,bad{i},split,1,5")]));

        (result.Value.Invalid, result.Value.Errors.Count).ShouldBe((60, 50));
        result.Value.Errors[0].Line.ShouldBe(2);
    }

    [Fact]
    public async Task Missing_columns_empty_text_and_too_many_lines_are_refused()
    {
        (await ImportAsync(null)).Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe("csv");
        (await ImportAsync("  \n ")).Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe("csv");
        (await ImportAsync("symbol,exDate,kind,numerator\nABB,2026-09-01,split,1")).Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe("header");
        (await ImportAsync(Csv([.. Enumerable.Repeat("ABB,2026-09-01,split,1,5", 5_001)]))).Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe("csv");
        (await ImportAsync(new string('x', 1_000_001))).Error.Code.ShouldBe("data.invalid");
        _data.Actions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Exactly_five_thousand_lines_are_accepted()
    {
        var abb = Listed("ABB");

        var result = await ImportAsync(Csv([.. Enumerable.Range(0, 5_000).Select(i => $"ABB,{new DateOnly(2013, 1, 1).AddDays(i):yyyy-MM-dd},split,1,5")]), preview: true);

        result.Value.Lines.ShouldBe(5_000);
        result.Value.Importable.ShouldBe(5_000);
        _ = abb;
    }

    [Fact]
    public async Task A_header_only_text_imports_nothing_without_an_error()
    {
        var result = await ImportAsync(Header);

        Counts(result.Value).ShouldBe((false, 0, 0, 0, 0, 0));
        _data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_save_that_collides_with_a_concurrent_import_is_planned_again_without_the_duplicates()
    {
        var abb = Listed("ABB");
        _data.Collisions.Enqueue(() => _data.Actions.Add(CorporateAction.Apply(abb, new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5, CorporateActionSource.Manual, null, Actor, Now)));

        var result = await ImportAsync(Csv("ABB,2026-09-01,split,1,5", "ABB,2026-08-01,split,1,2"));

        (result.Value.Imported, result.Value.Duplicates).ShouldBe((1, 1));
        _data.Actions.Count.ShouldBe(2);
        _data.Events.ShouldHaveSingleItem();
    }
}
