using XDrishti.Domain.Market.Actions;

namespace XDrishti.Domain.Tests.Market.Actions;

/// <summary>The life of a corporate action: applied by the owner, suggested by the detection, confirmed, edited or ignored — never deleted.</summary>
public sealed class CorporateActionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private static readonly Guid Instrument = Guid.NewGuid();

    private static readonly DateOnly ExDate = new(2026, 9, 1);

    [Fact]
    public void An_action_the_owner_states_is_applied_at_once_with_the_factor_stored()
    {
        var action = CorporateAction.Apply(Instrument, ExDate, CorporateActionKind.Split, 1, 5, CorporateActionSource.Manual, "  five for one  ", " owner@example.invalid ", Now);

        (action.Status, action.Source, action.Factor, action.Ratio, action.Note).ShouldBe((CorporateActionStatus.Applied, CorporateActionSource.Manual, 0.2m, "1:5", "five for one"));
        (action.DecidedAt, action.DecidedBy).ShouldBe((Now, "owner@example.invalid"));
    }

    [Fact]
    public void A_suggestion_has_no_decision_and_comes_from_the_detection()
    {
        var action = CorporateAction.Suggest(Instrument, ExDate, CorporateActionKind.Bonus, 1, 1, "seen", Now);

        (action.Status, action.Source, action.Factor, action.DecidedAt, action.DecidedBy).ShouldBe((CorporateActionStatus.Suggested, CorporateActionSource.Detected, 0.5m, null, null));
    }

    [Fact]
    public void Confirming_a_suggestion_with_changes_applies_it_and_recomputes_the_factor()
    {
        var action = CorporateAction.Suggest(Instrument, ExDate, CorporateActionKind.Split, 1, 2, null, Now);

        var changed = action.Confirm(ExDate.AddDays(1), CorporateActionKind.Bonus, 1, 1, "bonus", "owner", Now.AddHours(1));

        changed.ShouldBeTrue();
        (action.Status, action.ExDate, action.Kind, action.Ratio, action.Factor, action.DecidedAt, action.DecidedBy)
            .ShouldBe((CorporateActionStatus.Applied, ExDate.AddDays(1), CorporateActionKind.Bonus, "1:1", 0.5m, Now.AddHours(1), "owner"));
    }

    [Fact]
    public void Confirming_an_applied_action_with_the_same_values_changes_nothing()
    {
        var action = CorporateAction.Apply(Instrument, ExDate, CorporateActionKind.Split, 1, 5, CorporateActionSource.Csv, null, "owner", Now);

        action.Confirm(ExDate, CorporateActionKind.Split, 1, 5, null, "someone", Now.AddHours(1)).ShouldBeFalse();

        (action.DecidedAt, action.DecidedBy).ShouldBe((Now, "owner"));
    }

    [Fact]
    public void Ignoring_declines_the_action_once_and_confirming_brings_it_back()
    {
        var action = CorporateAction.Suggest(Instrument, ExDate, CorporateActionKind.Split, 1, 5, null, Now);

        action.Ignore("owner", Now.AddHours(1)).ShouldBeTrue();
        action.Ignore("owner", Now.AddHours(2)).ShouldBeFalse();
        action.Status.ShouldBe(CorporateActionStatus.Ignored);
        action.DecidedAt.ShouldBe(Now.AddHours(1));
        action.Confirm(ExDate, CorporateActionKind.Split, 1, 5, null, "owner", Now.AddHours(3)).ShouldBeTrue();
        action.Status.ShouldBe(CorporateActionStatus.Applied);
    }

    [Fact]
    public void An_invalid_ratio_or_date_is_refused()
    {
        Should.Throw<ArgumentException>(() => CorporateAction.Apply(Instrument, ExDate, CorporateActionKind.Split, 2, 2, CorporateActionSource.Manual, null, "o", Now));
        Should.Throw<ArgumentOutOfRangeException>(() => CorporateAction.Suggest(Instrument, new DateOnly(1999, 12, 31), CorporateActionKind.Split, 1, 2, null, Now));
    }

    [Fact]
    public void A_long_note_is_cut_to_the_limit_and_a_blank_one_is_dropped()
    {
        var long_ = CorporateAction.Apply(Instrument, ExDate, CorporateActionKind.Split, 1, 5, CorporateActionSource.Manual, new string('x', 300), "o", Now);
        var blank = CorporateAction.Apply(Instrument, ExDate, CorporateActionKind.Split, 1, 5, CorporateActionSource.Manual, "   ", "o", Now);

        long_.Note!.Length.ShouldBe(CorporateAction.MaxNoteLength);
        blank.Note.ShouldBeNull();
    }
}
