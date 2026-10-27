using XDrishti.Domain.Assistant;

namespace XDrishti.Domain.Tests.Assistant;

/// <summary>Spec P9-03 AC 8: the change is the owner's act with a reason and a budget in range; the classification of a change that needs the password is a pure function (table test).</summary>
public sealed class AssistantSettingsChangeTests
{
    private const string Owner = "owner:owner@example.invalid";

    [Theory]
    [InlineData("service:eval")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("owner:")]
    public void A_non_owner_actor_is_refused(string? actor) =>
        AssistantSettingsChange.Create(actor, "why", true, false, 6).Error.Code.ShouldBe("assistant.owner_only");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_reason_is_refused(string? reason) =>
        AssistantSettingsChange.Create(Owner, reason, true, false, 6).Error.Code.ShouldBe("assistant.reason_invalid");

    [Fact]
    public void A_reason_over_five_hundred_characters_is_refused_and_exactly_five_hundred_passes()
    {
        AssistantSettingsChange.Create(Owner, new string('x', 501), true, false, 6).Error.Code.ShouldBe("assistant.reason_invalid");
        AssistantSettingsChange.Create(Owner, new string('x', 500), true, false, 6).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(12, true)]
    [InlineData(13, false)]
    [InlineData(-1, false)]
    public void The_budget_is_one_to_twelve(int budget, bool valid)
    {
        var result = AssistantSettingsChange.Create(Owner, "why", true, false, budget);

        result.IsSuccess.ShouldBe(valid);
        if (!valid)
        {
            result.Error.Code.ShouldBe("assistant.values_invalid");
        }
    }

    [Fact]
    public void The_reason_is_trimmed() => AssistantSettingsChange.Create(Owner, "  because  ", true, false, 6).Value.Reason.ShouldBe("because");

    // current (enabled, portfolio, max) -> requested (enabled, portfolio, max) -> raises
    [Theory]
    [InlineData(false, false, 6, true, false, 6, true)] // turning the assistant on
    [InlineData(false, false, 6, false, true, 6, true)] // turning the portfolio on while the assistant is off is still raising
    [InlineData(true, false, 6, true, true, 6, true)]
    [InlineData(true, false, 6, true, false, 7, true)] // a larger budget
    [InlineData(true, true, 6, true, true, 6, false)] // nothing changes
    [InlineData(true, true, 6, false, true, 6, false)] // assistant off
    [InlineData(true, true, 6, true, false, 6, false)] // portfolio off
    [InlineData(true, true, 6, true, true, 5, false)] // lower budget
    [InlineData(true, true, 6, false, false, 1, false)] // everything reduced
    [InlineData(true, false, 6, false, false, 12, true)] // off, but the budget is raised
    [InlineData(false, false, 6, false, false, 6, false)] // off stays off
    public void Only_a_change_that_turns_something_on_or_raises_it_needs_the_password(bool enabled, bool portfolio, int max, bool newEnabled, bool newPortfolio, int newMax, bool raises) =>
        AssistantSettingsChange.RaisesExposure(new AssistantSettings(1, enabled, portfolio, max), newEnabled, newPortfolio, newMax).ShouldBe(raises);

    [Fact]
    public void With_no_row_the_defaults_are_off_and_a_budget_of_six()
    {
        var defaults = AssistantSettings.Defaults;

        (defaults.Id, defaults.Enabled, defaults.PortfolioContext, defaults.MaxToolCalls).ShouldBe((null, false, false, 6));
        AssistantSettingsChange.RaisesExposure(defaults, true, false, 6).ShouldBeTrue();
        AssistantSettingsChange.RaisesExposure(defaults, false, false, 6).ShouldBeFalse();
    }

    [Fact]
    public void Turning_a_write_switch_on_raises_exposure_and_turning_it_off_does_not()
    {
        var off = AssistantSettings.Defaults with { Enabled = true };
        var on = off with { AllowDraftConfirm = true, AutoLabQuickTest = true };

        AssistantSettingsChange.RaisesExposure(off, true, false, 6, allowDraftConfirm: true).ShouldBeTrue();
        AssistantSettingsChange.RaisesExposure(off, true, false, 6, autoLabQuickTest: true).ShouldBeTrue();
        AssistantSettingsChange.RaisesExposure(off, true, false, 6, allowProposalAccept: true).ShouldBeTrue();
        AssistantSettingsChange.RaisesExposure(off, true, false, 6, autoAcceptHypothesis: true).ShouldBeTrue();
        AssistantSettingsChange.RaisesExposure(on, true, false, 6, allowDraftConfirm: true, autoLabQuickTest: true).ShouldBeFalse();
        AssistantSettingsChange.RaisesExposure(on, true, false, 6).ShouldBeFalse();
    }

    [Fact]
    public void The_write_switches_default_to_off() =>
        AssistantSettings.Defaults.ShouldSatisfyAllConditions(
            d => d.AllowDraftConfirm.ShouldBeFalse(),
            d => d.AutoLabQuickTest.ShouldBeFalse(),
            d => d.AllowProposalAccept.ShouldBeFalse(),
            d => d.AutoAcceptHypothesis.ShouldBeFalse());
}
