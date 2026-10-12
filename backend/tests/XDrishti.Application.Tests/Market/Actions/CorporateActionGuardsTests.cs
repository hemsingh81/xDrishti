using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Tests.Market.Actions;

/// <summary>
/// The guards around applying an action: the owner's CSV files are already adjusted at source (a second adjustment needs the owner's consent) and a split and a bonus on one ex-date of one instrument
/// do not both apply (ADR 0017 §5).
/// </summary>
public sealed class CorporateActionGuardsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly Through = new(2026, 9, 30);

    private const string Actor = "owner@example.invalid";

    private const string Header = "symbol,exDate,kind,numerator,denominator";

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryActionData _data = new();
    private readonly FakeListedInstruments _listed = new();
    private readonly ActionInstrument _abb;

    public CorporateActionGuardsTests()
    {
        _abb = _data.AddInstrument("ABB");
        _listed.Matches["NSE:ABB"] = [_abb.Id];
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ValueTask<Result<CorporateActionView>> AddAsync(string exDate = "2026-09-01", string kind = "split", int numerator = 1, int denominator = 5, bool? acknowledge = null) =>
        new AddCorporateActionHandler(_data, _data, _data, _data, _clock)
            .HandleAsync(new AddCorporateActionCommand(new AddCorporateActionRequest(_abb.Id, exDate, kind, numerator, denominator, null, acknowledge), Actor), Ct);

    private ValueTask<Result<CorporateActionView>> UpdateAsync(Guid id, string? exDate = null, string? note = null, bool? acknowledge = null) =>
        new UpdateCorporateActionHandler(_data, _data, _data, _data, _clock)
            .HandleAsync(new UpdateCorporateActionCommand(id, new UpdateCorporateActionRequest(exDate, null, null, null, note, acknowledge), Actor), Ct);

    private ValueTask<Result<ImportCorporateActionsResult>> ImportAsync(bool preview, bool acknowledge, params string[] lines) =>
        new ImportCorporateActionsHandler(_listed, _data, _data, _data, _data, _clock)
            .HandleAsync(new ImportCorporateActionsCommand(Header + "\n" + string.Join('\n', lines), preview, Actor, acknowledge), Ct);

    private CorporateAction Suggested(string exDate, CorporateActionKind kind = CorporateActionKind.Split, int numerator = 1, int denominator = 5)
    {
        var action = CorporateAction.Suggest(_abb.Id, DateOnly.Parse(exDate, System.Globalization.CultureInfo.InvariantCulture), kind, numerator, denominator, "seen", Now.AddDays(-1));
        _data.Actions.Add(action);
        return action;
    }

    private CorporateAction Applied(string exDate, CorporateActionKind kind = CorporateActionKind.Bonus, int numerator = 1, int denominator = 1)
    {
        var action = CorporateAction.Apply(_abb.Id, DateOnly.Parse(exDate, System.Globalization.CultureInfo.InvariantCulture), kind, numerator, denominator, CorporateActionSource.Manual, null, Actor, Now);
        _data.Actions.Add(action);
        return action;
    }

    // ---- the loaded CSV files are adjusted already

    [Theory]
    [InlineData("2026-09-30", true)]   // the day the files end: on or before
    [InlineData("2026-09-01", true)]
    [InlineData("2026-10-01", false)]  // after the files
    public async Task Adding_an_action_on_or_before_the_date_the_loaded_files_reach_is_refused_with_the_counts_unless_acknowledged(string exDate, bool refused)
    {
        _data.CsvThrough[_abb.Id] = Through;

        var plain = await AddAsync(exDate);

        plain.IsFailure.ShouldBe(refused);
        if (refused)
        {
            plain.Error.Code.ShouldBe("data.csv_already_adjusted");
            plain.Error.Kind.ShouldBe(ErrorKind.Conflict);
            plain.Error.Data.ShouldNotBeNull().ShouldBe(new Dictionary<string, int> { ["adjustsCsvTwice"] = 1, ["instruments"] = 1 });
            plain.Error.Message.ShouldContain("2026-09-30");
            _data.Actions.ShouldBeEmpty(); // the refused one stored nothing
            (await AddAsync(exDate, acknowledge: true)).IsSuccess.ShouldBeTrue();
        }

        _data.Actions.ShouldHaveSingleItem().Status.ShouldBe(CorporateActionStatus.Applied);
    }

    [Fact]
    public async Task Without_loaded_files_nothing_is_adjusted_already()
    {
        (await AddAsync("2026-01-05")).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task The_views_of_an_added_action_carry_the_date_the_files_reach()
    {
        _data.CsvThrough[_abb.Id] = Through;

        var view = (await AddAsync("2026-11-01")).Value;

        view.CsvAdjustedThrough.ShouldBe(Through);
    }

    [Fact]
    public async Task Confirming_a_suggestion_inside_the_adjusted_part_needs_the_acknowledgement()
    {
        _data.CsvThrough[_abb.Id] = Through;
        var suggestion = Suggested("2026-09-01");

        var refused = await UpdateAsync(suggestion.Id);
        suggestion.Status.ShouldBe(CorporateActionStatus.Suggested);
        var accepted = await UpdateAsync(suggestion.Id, acknowledge: true);

        refused.Error.Code.ShouldBe("data.csv_already_adjusted");
        accepted.Value.Status.ShouldBe("applied");
    }

    [Fact]
    public async Task Moving_an_ex_date_into_the_adjusted_part_needs_the_acknowledgement_but_a_note_change_of_an_applied_action_does_not()
    {
        _data.CsvThrough[_abb.Id] = Through;
        var applied = Applied("2026-11-02", CorporateActionKind.Split, 1, 5);
        var old = Applied("2026-08-03", CorporateActionKind.Split, 1, 2); // applied earlier, with consent

        var moved = await UpdateAsync(applied.Id, exDate: "2026-09-15");
        var noted = await UpdateAsync(old.Id, note: "split of August");

        moved.Error.Code.ShouldBe("data.csv_already_adjusted");
        noted.IsSuccess.ShouldBeTrue();
        noted.Value.CsvAdjustedThrough.ShouldBe(Through);
    }

    [Fact]
    public async Task The_list_items_carry_the_date_each_instruments_files_reach()
    {
        var tcs = _data.AddInstrument("TCS");
        _data.CsvThrough[_abb.Id] = Through;
        Applied("2026-09-01");
        _data.Actions.Add(CorporateAction.Apply(tcs.Id, new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5, CorporateActionSource.Manual, null, Actor, Now));

        var page = (await new ListCorporateActionsHandler(_data).HandleAsync(new ListCorporateActionsQuery(new CorporateActionRequest(null, null, null, null)), Ct)).Value;

        page.Items.ToDictionary(i => i.Symbol, i => i.CsvAdjustedThrough).ShouldBe(new Dictionary<string, DateOnly?> { ["NSE:ABB"] = Through, ["NSE:TCS"] = null });
    }

    [Fact]
    public async Task An_import_preview_counts_and_lists_the_lines_that_would_adjust_the_files_twice_per_line_with_the_date()
    {
        _data.CsvThrough[_abb.Id] = Through;

        var result = (await ImportAsync(true, false, "ABB,2026-09-01,split,1,5", "ABB,2026-11-02,bonus,1,1")).Value;

        (result.Preview, result.Importable, result.AdjustsCsvTwice).ShouldBe((true, 2, 1));
        var warning = result.Warnings.ShouldHaveSingleItem();
        (warning.Line, warning.Symbol, warning.ExDate, warning.CsvAdjustedThrough).ShouldBe((2, "NSE:ABB", new DateOnly(2026, 9, 1), Through));
        warning.Message.ShouldContain("2026-09-30");
        _data.Actions.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_import_with_such_lines_is_refused_with_the_counts_unless_acknowledged_and_then_imports_everything()
    {
        _data.CsvThrough[_abb.Id] = Through;
        string[] lines = ["ABB,2026-09-01,split,1,5", "ABB,2026-08-03,bonus,1,1", "ABB,2026-11-02,split,1,2"];

        var refused = await ImportAsync(false, false, lines);
        var storedAfterRefusal = _data.Actions.Count;
        var accepted = await ImportAsync(false, true, lines);

        refused.Error.Code.ShouldBe("data.csv_already_adjusted");
        refused.Error.Data.ShouldNotBeNull().ShouldBe(new Dictionary<string, int> { ["adjustsCsvTwice"] = 2, ["instruments"] = 1 });
        storedAfterRefusal.ShouldBe(0);
        (accepted.Value.Imported, accepted.Value.AdjustsCsvTwice).ShouldBe((3, 2));
    }

    [Fact]
    public async Task Duplicates_that_are_skipped_do_not_count_as_adjusting_twice()
    {
        _data.CsvThrough[_abb.Id] = Through;
        Applied("2026-09-01", CorporateActionKind.Split, 1, 5);

        var result = await ImportAsync(false, false, "ABB,2026-09-01,split,1,5", "ABB,2026-11-02,split,1,2");

        result.IsSuccess.ShouldBeTrue();
        (result.Value.Imported, result.Value.Duplicates, result.Value.AdjustsCsvTwice).ShouldBe((1, 1, 0));
    }

    [Fact]
    public async Task At_most_fifty_warnings_are_listed_but_all_are_counted()
    {
        _data.CsvThrough[_abb.Id] = Through;
        var lines = Enumerable.Range(0, 60).Select(i => $"ABB,{new DateOnly(2026, 1, 1).AddDays(i):yyyy-MM-dd},split,1,{i + 2}").ToArray();

        var result = (await ImportAsync(true, false, lines)).Value;

        (result.AdjustsCsvTwice, result.Warnings!.Count).ShouldBe((60, 50));
    }

    // ---- a split and a bonus on the same day

    [Fact]
    public async Task Adding_one_kind_while_the_other_is_applied_on_the_same_day_is_refused()
    {
        Applied("2026-09-01");

        var result = await AddAsync("2026-09-01", "split", 1, 5);

        result.Error.Code.ShouldBe("data.corporate_action_conflicts_same_day");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        _data.Actions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Once_the_applied_one_is_ignored_the_other_kind_can_be_added()
    {
        var bonus = Applied("2026-09-01");
        bonus.Ignore(Actor, Now);

        (await AddAsync("2026-09-01", "split", 1, 5)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_pending_suggestion_of_the_other_kind_is_declined_by_itself_and_audited_when_one_is_added()
    {
        var guess = Suggested("2026-09-01", CorporateActionKind.Bonus, 4, 1);

        var result = await AddAsync("2026-09-01", "split", 1, 5);

        result.IsSuccess.ShouldBeTrue();
        guess.Status.ShouldBe(CorporateActionStatus.Ignored);
        _data.Events.Select(e => e.Event).ShouldBe([AccountEventType.CorporateActionIgnored, AccountEventType.CorporateActionAdded]);
        _data.Events[0].Detail.ShouldBe("Bonus 4:1 NSE:ABB ex 2026-09-01 (detected) declined: the other kind was applied on the same day");
    }

    [Fact]
    public async Task Another_day_or_an_ignored_action_of_the_other_kind_is_no_conflict()
    {
        Applied("2026-09-02");
        var declined = Suggested("2026-09-01", CorporateActionKind.Bonus, 1, 1);
        declined.Ignore(Actor, Now);

        var result = await AddAsync("2026-09-01", "split", 1, 5);

        result.IsSuccess.ShouldBeTrue();
        declined.Status.ShouldBe(CorporateActionStatus.Ignored);
    }

    [Fact]
    public async Task Confirming_a_suggestion_declines_a_pending_one_of_the_other_kind_and_is_refused_against_an_applied_one()
    {
        var split = Suggested("2026-09-01");
        var guess = Suggested("2026-09-01", CorporateActionKind.Bonus, 4, 1);
        var later = Suggested("2026-09-08", CorporateActionKind.Split, 1, 2);
        Applied("2026-09-08");

        var confirmed = await UpdateAsync(split.Id);
        var refused = await UpdateAsync(later.Id);

        confirmed.Value.Status.ShouldBe("applied");
        guess.Status.ShouldBe(CorporateActionStatus.Ignored);
        refused.Error.Code.ShouldBe("data.corporate_action_conflicts_same_day");
        later.Status.ShouldBe(CorporateActionStatus.Suggested);
    }

    [Fact]
    public async Task Editing_an_ex_date_onto_a_day_with_an_applied_action_of_the_other_kind_is_refused()
    {
        Applied("2026-09-08");
        var split = Applied("2026-09-01", CorporateActionKind.Split, 1, 5);

        var result = await UpdateAsync(split.Id, exDate: "2026-09-08");

        result.Error.Code.ShouldBe("data.corporate_action_conflicts_same_day");
        split.ExDate.ShouldBe(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task An_import_line_against_an_applied_action_of_the_other_kind_is_a_problem_line_and_the_rest_is_imported()
    {
        Applied("2026-09-01");

        var result = (await ImportAsync(false, false, "ABB,2026-09-01,split,1,5", "ABB,2026-10-05,split,1,2")).Value;

        (result.Imported, result.Invalid).ShouldBe((1, 1));
        var error = result.Errors.ShouldHaveSingleItem();
        error.Line.ShouldBe(2); // line 1 is the header
        error.Message.ShouldStartWith("kind:");
    }

    [Fact]
    public async Task Two_lines_of_different_kinds_on_one_day_import_the_first_and_refuse_the_second()
    {
        var result = (await ImportAsync(false, false, "ABB,2026-09-01,split,1,5", "ABB,2026-09-01,bonus,1,1")).Value;

        (result.Imported, result.Invalid, result.Errors.Single().Line).ShouldBe((1, 1, 3));
        _data.Actions.ShouldHaveSingleItem().Kind.ShouldBe(CorporateActionKind.Split);
    }

    [Fact]
    public async Task An_import_declines_a_pending_suggestion_of_the_other_kind_only_when_it_really_imports()
    {
        var guess = Suggested("2026-09-01", CorporateActionKind.Bonus, 4, 1);

        await ImportAsync(true, false, "ABB,2026-09-01,split,1,5");
        var afterPreview = guess.Status;
        var result = await ImportAsync(false, false, "ABB,2026-09-01,split,1,5");

        afterPreview.ShouldBe(CorporateActionStatus.Suggested);
        result.Value.Imported.ShouldBe(1);
        guess.Status.ShouldBe(CorporateActionStatus.Ignored);
        _data.Events.Select(e => e.Event).ShouldBe([AccountEventType.CorporateActionIgnored, AccountEventType.CorporateActionsImported]);
    }

    // ---- the detection does not bring back a date the owner moved away from

    [Fact]
    public void A_detected_suggestion_remembers_the_date_it_was_found_on_through_an_edit()
    {
        var suggestion = Suggested("2026-09-01");

        suggestion.Confirm(new DateOnly(2026, 9, 2), CorporateActionKind.Split, 1, 5, null, Actor, Now);

        (suggestion.ExDate, suggestion.DetectedExDate).ShouldBe((new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 1)));
        CorporateAction.Apply(_abb.Id, new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5, CorporateActionSource.Manual, null, Actor, Now).DetectedExDate.ShouldBeNull();
    }

    [Fact]
    public async Task The_known_dates_of_the_detection_include_the_date_an_edited_suggestion_was_found_on()
    {
        var suggestion = Suggested("2026-09-01");
        suggestion.Confirm(new DateOnly(2026, 9, 2), CorporateActionKind.Split, 1, 5, null, Actor, Now);

        var known = await _data.ListExDatesAsync(_abb.Id, Ct);

        known.OrderBy(d => d).ShouldBe([new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2)]);
    }
}
