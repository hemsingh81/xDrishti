using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Tests.Market.Actions;

/// <summary>Adding, confirming, editing and ignoring corporate actions: validation, uniqueness, idempotency and the audit trail (ADR 0017 §5).</summary>
public sealed class CorporateActionHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero); // Monday 12:00 India

    private const string Actor = "owner@example.invalid";

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryActionData _data = new();
    private readonly ActionInstrument _abb;

    public CorporateActionHandlersTests() => _abb = _data.AddInstrument("ABB");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AddCorporateActionHandler Add => new(_data, _data, _data, _data, _clock);

    private UpdateCorporateActionHandler Update => new(_data, _data, _data, _data, _clock);

    private IgnoreCorporateActionHandler Ignore => new(_data, _data, _data, _data, _clock);

    private ValueTask<XDrishti.Domain.Common.Result<CorporateActionView>> AddAsync(Guid? instrument, string? exDate = "2026-09-01", string? kind = "split", int? numerator = 1, int? denominator = 5, string? note = null) =>
        Add.HandleAsync(new AddCorporateActionCommand(new AddCorporateActionRequest(instrument, exDate, kind, numerator, denominator, note), Actor), Ct);

    private ValueTask<XDrishti.Domain.Common.Result<CorporateActionView>> UpdateAsync(Guid id, string? exDate = null, string? kind = null, int? numerator = null, int? denominator = null, string? note = null) =>
        Update.HandleAsync(new UpdateCorporateActionCommand(id, new UpdateCorporateActionRequest(exDate, kind, numerator, denominator, note), Actor), Ct);

    private CorporateAction Suggested(DateOnly? exDate = null, CorporateActionKind kind = CorporateActionKind.Split, int numerator = 1, int denominator = 5)
    {
        var action = CorporateAction.Suggest(_abb.Id, exDate ?? new DateOnly(2026, 9, 1), kind, numerator, denominator, "seen", Now.AddDays(-1));
        _data.Actions.Add(action);
        return action;
    }

    [Fact]
    public async Task Adding_stores_an_applied_manual_action_with_its_factor_and_audits_it_without_the_note()
    {
        var result = await AddAsync(_abb.Id, note: "  five for one, Sept  ");

        result.IsSuccess.ShouldBeTrue();
        var view = result.Value;
        (view.Symbol, view.Name, view.ExDate, view.Kind, view.Ratio, view.Factor, view.Status, view.Source, view.Note).ShouldBe(("NSE:ABB", "ABB Ltd", new DateOnly(2026, 9, 1), "split", "1:5", 0.2, "applied", "manual", "five for one, Sept"));
        (view.CreatedAt, view.DecidedAt, view.DecidedBy).ShouldBe((Now, Now, Actor));
        _data.Actions.ShouldHaveSingleItem().Status.ShouldBe(CorporateActionStatus.Applied);
        var audit = _data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Detail, audit.Actor, audit.AccountId).ShouldBe((AccountEventType.CorporateActionAdded, "Split 1:5 NSE:ABB ex 2026-09-01 (manual)", Actor, null));
    }

    [Fact]
    public async Task A_bonus_one_for_one_has_a_factor_of_one_half()
    {
        var result = await AddAsync(_abb.Id, kind: "BONUS", numerator: 1, denominator: 1);

        (result.Value.Kind, result.Value.Factor).ShouldBe(("bonus", 0.5));
        _data.Events.Single().Detail.ShouldBe("Bonus 1:1 NSE:ABB ex 2026-09-01 (manual)");
    }

    [Fact]
    public async Task Every_problem_of_a_request_comes_back_with_its_field()
    {
        var result = await AddAsync(null, exDate: "01/09/2026", kind: "dividend", numerator: 0, denominator: 100_001, note: new string('x', 201));

        result.Error.Code.ShouldBe("data.invalid");
        result.Error.Fields!.Select(f => f.Field).ShouldBe(["instrumentId", "exDate", "kind", "numerator", "denominator", "note"]);
        _data.Actions.ShouldBeEmpty();
        _data.Events.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("1999-12-31")]
    [InlineData("2027-10-13")]
    public async Task An_ex_date_before_the_data_starts_or_more_than_a_year_ahead_is_refused(string date)
    {
        var result = await AddAsync(_abb.Id, exDate: date);

        result.Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe("exDate");
    }

    [Fact]
    public async Task An_ex_date_up_to_a_year_ahead_is_accepted_for_an_announced_action()
    {
        (await AddAsync(_abb.Id, exDate: "2027-10-12")).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_split_one_to_one_and_a_note_with_control_characters_are_refused()
    {
        var same = await AddAsync(_abb.Id, numerator: 3, denominator: 3);
        var control = await AddAsync(_abb.Id, note: "a\u0001b");

        same.Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe("denominator");
        control.Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe("note");
    }

    [Fact]
    public async Task An_unknown_instrument_is_not_found()
    {
        var result = await AddAsync(Guid.NewGuid());

        result.Error.Code.ShouldBe("data.instrument_not_found");
    }

    [Theory]
    [InlineData(CorporateActionStatus.Suggested)]
    [InlineData(CorporateActionStatus.Applied)]
    [InlineData(CorporateActionStatus.Ignored)]
    public async Task An_instrument_cannot_have_two_actions_of_one_kind_on_one_ex_date_whatever_the_status_of_the_first(CorporateActionStatus status)
    {
        var existing = Suggested();
        if (status == CorporateActionStatus.Applied)
        {
            existing.Confirm(existing.ExDate, existing.Kind, existing.Numerator, existing.Denominator, null, Actor, Now);
        }
        else if (status == CorporateActionStatus.Ignored)
        {
            existing.Ignore(Actor, Now);
        }

        var result = await AddAsync(_abb.Id, numerator: 1, denominator: 2);

        result.Error.Code.ShouldBe("data.corporate_action_exists");
        _data.Actions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_bonus_on_the_date_of_a_split_is_a_different_action()
    {
        Suggested();

        var result = await AddAsync(_abb.Id, kind: "bonus", numerator: 1, denominator: 1);

        result.IsSuccess.ShouldBeTrue();
        _data.Actions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_save_that_collides_with_a_concurrent_add_is_read_again_and_reported_as_existing()
    {
        _data.Collisions.Enqueue(() => _data.Actions.Add(CorporateAction.Apply(_abb.Id, new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5, CorporateActionSource.Manual, null, Actor, Now)));

        var result = await AddAsync(_abb.Id);

        result.Error.Code.ShouldBe("data.corporate_action_exists");
        _data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Confirming_a_suggestion_with_an_empty_body_applies_it_as_it_is_and_audits_a_confirmation()
    {
        var action = Suggested();

        var result = await UpdateAsync(action.Id);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.Status, result.Value.Source, result.Value.Ratio, result.Value.Note, result.Value.DecidedBy).ShouldBe(("applied", "detected", "1:5", "seen", Actor));
        var audit = _data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Detail).ShouldBe((AccountEventType.CorporateActionConfirmed, "Split 1:5 NSE:ABB ex 2026-09-01 (detected)"));
    }

    [Fact]
    public async Task Confirming_with_another_ratio_kind_and_date_changes_them_and_the_factor()
    {
        var action = Suggested(numerator: 1, denominator: 2);

        var result = await UpdateAsync(action.Id, exDate: "2026-09-02", kind: "bonus", numerator: 1, denominator: 1, note: "bonus 1:1");

        (result.Value.ExDate, result.Value.Kind, result.Value.Ratio, result.Value.Factor, result.Value.Status, result.Value.Note).ShouldBe((new DateOnly(2026, 9, 2), "bonus", "1:1", 0.5, "applied", "bonus 1:1"));
        _data.Events.Single().Event.ShouldBe(AccountEventType.CorporateActionConfirmed);
    }

    [Fact]
    public async Task An_empty_note_clears_the_note_and_a_missing_one_keeps_it()
    {
        var action = Suggested();

        (await UpdateAsync(action.Id, numerator: 1, denominator: 4)).Value.Note.ShouldBe("seen");
        (await UpdateAsync(action.Id, note: "")).Value.Note.ShouldBeNull();
    }

    [Fact]
    public async Task Editing_an_applied_action_audits_an_edit_naming_the_old_values()
    {
        var action = Suggested();
        await UpdateAsync(action.Id);

        var result = await UpdateAsync(action.Id, numerator: 1, denominator: 10);

        result.Value.Ratio.ShouldBe("1:10");
        var audit = _data.Events.Last();
        (audit.Event, audit.Detail).ShouldBe((AccountEventType.CorporateActionEdited, "Split 1:10 NSE:ABB ex 2026-09-01 (detected) (was split 1:5 ex 2026-09-01)"));
    }

    [Fact]
    public async Task Confirming_again_changes_nothing_and_is_not_audited_again()
    {
        var action = Suggested();
        await UpdateAsync(action.Id);

        var again = await UpdateAsync(action.Id);

        again.IsSuccess.ShouldBeTrue();
        _data.Events.Count.ShouldBe(1);
        _data.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task An_ignored_action_can_be_brought_back_by_confirming_it()
    {
        var action = Suggested();
        await Ignore.HandleAsync(new IgnoreCorporateActionCommand(action.Id, Actor), Ct);

        var result = await UpdateAsync(action.Id);

        result.Value.Status.ShouldBe("applied");
        _data.Events.Select(e => e.Event).ShouldBe([AccountEventType.CorporateActionIgnored, AccountEventType.CorporateActionConfirmed]);
    }

    [Fact]
    public async Task Moving_an_action_onto_the_ex_date_of_another_of_the_same_kind_is_a_conflict()
    {
        Suggested(new DateOnly(2026, 9, 1));
        var other = Suggested(new DateOnly(2026, 8, 1));

        var result = await UpdateAsync(other.Id, exDate: "2026-09-01");

        result.Error.Code.ShouldBe("data.corporate_action_exists");
        other.ExDate.ShouldBe(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public async Task Editing_an_action_that_does_not_exist_is_not_found_and_bad_values_come_back_by_field()
    {
        (await UpdateAsync(Guid.NewGuid())).Error.Code.ShouldBe("data.corporate_action_not_found");

        var action = Suggested();
        var bad = await UpdateAsync(action.Id, exDate: "soon", kind: "x", numerator: 0);

        bad.Error.Fields!.Select(f => f.Field).ShouldBe(["exDate", "kind", "numerator"]);
        action.Status.ShouldBe(CorporateActionStatus.Suggested);
    }

    [Fact]
    public async Task Changing_the_kind_to_a_split_with_the_same_numbers_is_checked_against_the_stored_ratio()
    {
        var action = Suggested(kind: CorporateActionKind.Bonus, numerator: 1, denominator: 1);

        var result = await UpdateAsync(action.Id, kind: "split");

        result.Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe("denominator");
    }

    [Fact]
    public async Task Ignoring_a_suggestion_keeps_it_as_ignored_and_audits_it()
    {
        var action = Suggested();

        var result = await Ignore.HandleAsync(new IgnoreCorporateActionCommand(action.Id, Actor), Ct);

        (result.Value.Status, result.Value.DecidedBy).ShouldBe(("ignored", Actor));
        _data.Actions.ShouldHaveSingleItem();
        var audit = _data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Detail).ShouldBe((AccountEventType.CorporateActionIgnored, "Split 1:5 NSE:ABB ex 2026-09-01 (detected)"));
    }

    [Fact]
    public async Task Ignoring_twice_is_not_audited_twice_and_an_applied_action_can_be_taken_back()
    {
        var action = Suggested();
        await UpdateAsync(action.Id);

        await Ignore.HandleAsync(new IgnoreCorporateActionCommand(action.Id, Actor), Ct);
        var again = await Ignore.HandleAsync(new IgnoreCorporateActionCommand(action.Id, Actor), Ct);

        again.Value.Status.ShouldBe("ignored");
        _data.Events.Select(e => e.Event).ShouldBe([AccountEventType.CorporateActionConfirmed, AccountEventType.CorporateActionIgnored]);
    }

    [Fact]
    public async Task Ignoring_an_unknown_action_is_not_found()
    {
        var result = await Ignore.HandleAsync(new IgnoreCorporateActionCommand(Guid.NewGuid(), Actor), Ct);

        result.Error.Code.ShouldBe("data.corporate_action_not_found");
    }

    [Fact]
    public async Task The_list_filters_by_status_and_instrument_pages_and_counts_the_pending_suggestions()
    {
        var tcs = _data.AddInstrument("TCS");
        Suggested(new DateOnly(2026, 9, 1));
        var applied = CorporateAction.Apply(tcs.Id, new DateOnly(2026, 8, 1), CorporateActionKind.Bonus, 1, 1, CorporateActionSource.Csv, null, Actor, Now);
        _data.Actions.Add(applied);
        var handler = new ListCorporateActionsHandler(_data);

        var all = await handler.HandleAsync(new ListCorporateActionsQuery(new CorporateActionRequest(null, null, null, null)), Ct);
        var suggested = await handler.HandleAsync(new ListCorporateActionsQuery(new CorporateActionRequest(" Suggested ", null, null, null)), Ct);
        var tcsOnly = await handler.HandleAsync(new ListCorporateActionsQuery(new CorporateActionRequest(null, tcs.Id, 1, 1)), Ct);

        all.Value.Items.Select(i => i.Symbol).ShouldBe(["NSE:ABB", "NSE:TCS"]);
        (all.Value.Total, all.Value.Pending, all.Value.Page, all.Value.PageSize).ShouldBe((2, 1, 1, 50));
        suggested.Value.Items.ShouldHaveSingleItem().Symbol.ShouldBe("NSE:ABB");
        tcsOnly.Value.Items.ShouldHaveSingleItem().Status.ShouldBe("applied");
    }

    [Fact]
    public void An_unreadable_list_request_is_invalid_by_field()
    {
        var result = ListCorporateActionsHandler.Check(new CorporateActionRequest("weird", null, 0, 201));

        result.Error.Code.ShouldBe("data.invalid");
        result.Error.Fields!.Select(f => f.Field).ShouldBe(["status", "page", "pageSize"]);
        ListCorporateActionsHandler.Check(new CorporateActionRequest("ignored", null, CorporateActionSearch.MaxPage, CorporateActionSearch.MaxPageSize)).IsSuccess.ShouldBeTrue();
    }
}
