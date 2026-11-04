using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-06, D1 and AC2: five non-empty plain-text parts, length-limited, no control characters, hashed.</summary>
public sealed class RegistrationTests
{
    private static Common.Result<Registration> Make(string? a = "forced seller", string? b = "period: negative", string? c = "tier A, open", string? d = "40 bps gross, costs 12 bps", string? e = "retire if OOS under 0.05 R") =>
        Registration.Create(a, b, c, d, e);

    [Fact]
    public void Five_filled_parts_make_a_registration_and_the_text_is_trimmed()
    {
        var made = Make("  forced seller  ");

        made.IsSuccess.ShouldBeTrue();
        made.Value.Counterparty.ShouldBe("forced seller");
        made.Value.Parts.Count.ShouldBe(5);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t ")]
    public void An_empty_part_is_refused_and_named(string? empty)
    {
        var made = Make(e: empty);

        made.IsFailure.ShouldBeTrue();
        made.Error.Fields!.Single().Field.ShouldBe("falsifier");
    }

    [Fact]
    public void Every_problem_is_listed_not_only_the_first()
    {
        var made = Registration.Create(null, new string('x', Registration.MaxPartLength + 1), "bad\u0007text", "ok", "ok");

        made.Error.Fields!.Select(f => f.Field).ShouldBe(["counterparty", "parameterSigns", "applicability"]);
    }

    [Fact]
    public void The_longest_allowed_part_is_accepted_and_one_more_is_not()
    {
        Make(a: new string('x', Registration.MaxPartLength)).IsSuccess.ShouldBeTrue();
        Make(a: new string('x', Registration.MaxPartLength + 1)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Line_feeds_and_tabs_are_plain_text_but_other_control_characters_are_not()
    {
        Make(b: "a\nb\tc\r\nd").IsSuccess.ShouldBeTrue();
        Make(b: "a\u0000b").IsFailure.ShouldBeTrue();
        Make(b: "a\u001bb").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_text_hash_is_stable_sensitive_and_cannot_be_forged_by_moving_text_between_parts()
    {
        var a = Make().Value;

        a.TextHash().ShouldBe(Make().Value.TextHash());
        a.TextHash().Length.ShouldBe(64);
        a.TextHash().ShouldNotBe(Make(e: "retire if OOS under 0.06 R").Value.TextHash());
        Make(a: "ab", b: "c").Value.TextHash().ShouldNotBe(Make(a: "a", b: "bc").Value.TextHash());
    }
}
