using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>Spec P7-13: only an exit label spelt as one candidate is replayed; anything else is "not gradable", never guessed.</summary>
public sealed class ExitLabelTests
{
    [Theory]
    [InlineData("FIXED_RR{rr=2}")]
    [InlineData("FIXED_RR{rr=1.5}")]
    [InlineData("ATR_TRAIL{mult=2}")]
    public void A_candidate_spelling_is_read(string label)
    {
        ExitLabel.TryRead(label, out var choice, out _).ShouldBeTrue();

        choice.Candidates.Count.ShouldBe(1);
        choice.Candidates[0].Id.ShouldBe(label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("FIXED_RR")]
    [InlineData("FIXED_RR{rr=2}+time_stop")]
    [InlineData("FIXED_RR{rr=2.0}")]
    [InlineData("FIXED_RR{rr=2|3}")]
    [InlineData("NOPE{x=1}")]
    [InlineData("FIXED_RR{}")]
    public void Anything_else_is_refused_with_a_reason(string label)
    {
        ExitLabel.TryRead(label, out var choice, out var reason).ShouldBeFalse();

        choice.ShouldBeNull();
        reason.ShouldNotBeEmpty();
    }
}
