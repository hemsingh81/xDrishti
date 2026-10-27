using XDrishti.Domain.Assistant.Proposals;

namespace XDrishti.Domain.Tests.Assistant.Proposals;

/// <summary>Spec P9-08 AC 8: expiry is computed on read; nothing is updated.</summary>
public sealed class ProposalLifecycleTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_hypothesis_lives_seven_days_and_a_basket_change_three()
    {
        ProposalLifecycle.ExpiryOf(ProposalKind.Hypothesis, Created).ShouldBe(Created.AddDays(7));
        ProposalLifecycle.ExpiryOf(ProposalKind.BasketAdd, Created).ShouldBe(Created.AddDays(3));
        ProposalLifecycle.ExpiryOf(ProposalKind.BasketRemove, Created).ShouldBe(Created.AddDays(3));
    }

    [Fact]
    public void The_state_is_the_decision_or_the_clock()
    {
        var expires = Created.AddDays(3);

        ProposalLifecycle.StateOf(null, expires, Created).ShouldBe("pending");
        ProposalLifecycle.StateOf(null, expires, expires).ShouldBe("expired");
        ProposalLifecycle.StateOf(null, expires, expires.AddDays(1)).ShouldBe("expired");
        ProposalLifecycle.StateOf("accepted", expires, expires.AddDays(9)).ShouldBe("accepted");
        ProposalLifecycle.StateOf("rejected", expires, Created).ShouldBe("rejected");
    }
}
