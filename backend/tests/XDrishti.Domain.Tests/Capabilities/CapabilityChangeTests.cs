using XDrishti.Domain.Capabilities;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Tests.Capabilities;

/// <summary>Spec P8-18 Part A (ADR 0128): who may switch which capability, and the names the gate knows.</summary>
public sealed class CapabilityChangeTests
{
    private const string Owner = "owner:test@example.com";

    [Fact]
    public void The_whitelist_is_the_five_names_of_the_owner_act_slices_with_a_label_and_a_meaning_each()
    {
        XDrishti.Domain.Capabilities.Capabilities.All.Select(c => c.Name).ShouldBe(["experiments_ui", "learning_moves_ui", "autonomy_form", "exports", "pwa"]);
        XDrishti.Domain.Capabilities.Capabilities.All.ShouldAllBe(c => c.Label.Length > 0 && c.Meaning.Length > 0 && c.Name.Length <= XDrishti.Domain.Capabilities.Capabilities.MaxNameLength);
        XDrishti.Domain.Capabilities.Capabilities.All.Select(c => c.Name).Distinct().Count().ShouldBe(5);
    }

    [Theory]
    [InlineData("experiments_ui", true)]
    [InlineData("pwa", true)]
    [InlineData("Experiments_UI", false)]
    [InlineData("experiments_ui ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("apply", false)]
    public void Only_an_exact_name_of_the_whitelist_is_known(string? name, bool known) =>
        XDrishti.Domain.Capabilities.Capabilities.IsKnown(name).ShouldBe(known);

    [Fact]
    public void An_owner_actor_a_known_name_and_a_reason_make_a_change_with_the_reason_trimmed()
    {
        var change = CapabilityChange.Create("exports", true, Owner, "  I want to save a table  ");

        change.IsSuccess.ShouldBeTrue();
        change.Value.Name.ShouldBe("exports");
        change.Value.Enabled.ShouldBeTrue();
        change.Value.Actor.ShouldBe(Owner);
        change.Value.Reason.ShouldBe("I want to save a table");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cli:xd")]
    [InlineData("rule:autonomy")]
    [InlineData("owner:")]
    [InlineData("owner: ")]
    [InlineData("job:experiment")]
    public void Only_an_owner_actor_switches_a_capability_never_a_verb_a_rule_or_a_job(string? actor)
    {
        var change = CapabilityChange.Create("exports", true, actor, "why");

        change.IsFailure.ShouldBeTrue();
        change.Error.Code.ShouldBe("capabilities.owner_only");
        change.Error.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public void An_unknown_name_is_not_found_and_the_message_does_not_echo_control_characters()
    {
        var change = CapabilityChange.Create("nothing\u001b[31m", true, Owner, "why");

        change.Error.Code.ShouldBe("capabilities.not_found");
        change.Error.Kind.ShouldBe(ErrorKind.NotFound);
        change.Error.Message.ShouldNotContain("\u001b");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("line one\nline two")]
    public void A_missing_or_control_character_reason_is_refused(string? reason) =>
        CapabilityChange.Create("exports", true, Owner, reason).Error.Code.ShouldBe("capabilities.invalid");

    [Fact]
    public void A_reason_of_500_characters_fits_and_501_does_not()
    {
        CapabilityChange.Create("exports", false, Owner, new string('x', 500)).IsSuccess.ShouldBeTrue();
        CapabilityChange.Create("exports", false, Owner, new string('x', 501)).Error.Code.ShouldBe("capabilities.invalid");
    }

    [Fact]
    public void The_off_refusal_names_the_capability_is_a_403_and_says_it_is_off_by_default()
    {
        var error = CapabilityErrors.Off("experiments_ui");

        error.Code.ShouldBe("capabilities.off");
        error.Kind.ShouldBe(ErrorKind.Forbidden);
        error.Message.ShouldContain("experiments_ui");
        error.Message.ShouldContain("off by default");
    }
}
