using XDrishti.Application.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Tests.Lab;

/// <summary>AU-11 (ADR 0243): training sweeps are shown beside the honest count and say they are not in it; they never move the total.</summary>
public sealed class TrialsViewTrainingSweepTests
{
    [Fact]
    public void Training_sweeps_are_named_in_the_text_and_are_not_in_the_total()
    {
        var plain = TrialsView.Of(new TrialCounts(2, 1, 0, 0, 0), false, true);
        var withSweeps = TrialsView.Of(new TrialCounts(2, 1, 0, 0, 0, 3, 40), false, true);

        withSweeps.Total.ShouldBe(plain.Total);
        withSweeps.HonestFactor.ShouldBe(plain.HonestFactor);
        withSweeps.Text.ShouldStartWith(plain.Text);
        withSweeps.Text.ShouldContain("3 training sweeps (40 variants) are recorded separately and are not in this count");
        plain.Text.ShouldNotContain("training sweeps");
    }
}
