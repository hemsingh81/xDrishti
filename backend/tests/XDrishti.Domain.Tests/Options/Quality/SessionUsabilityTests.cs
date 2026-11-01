using XDrishti.Domain.Options.Quality;

namespace XDrishti.Domain.Tests.Options.Quality;

public sealed class SessionUsabilityTests
{
    private static QualityCheck Check(string code, QualityStatus status) => new(code, status, null, null, status == QualityStatus.Unavailable ? "not_recorded" : null, 1, null);

    [Theory]
    [InlineData(QualityStatus.Pass, QualityStatus.Pass, true)]
    [InlineData(QualityStatus.Pass, QualityStatus.Warn, true)]
    [InlineData(QualityStatus.Pass, QualityStatus.Fail, false)]
    [InlineData(QualityStatus.Pass, QualityStatus.Unavailable, false)]
    [InlineData(QualityStatus.Warn, QualityStatus.Pass, false)]
    [InlineData(QualityStatus.Fail, QualityStatus.Pass, false)]
    [InlineData(QualityStatus.Unavailable, QualityStatus.Pass, false)]
    public void A_chain_session_is_usable_when_coverage_passes_and_parity_passes_or_warns(QualityStatus coverage, QualityStatus parity, bool usable)
    {
        SessionUsability.IsUsable("chain", [Check("coverage", coverage), Check("parity", parity), Check("greeks", QualityStatus.Fail)]).ShouldBe(usable);
    }

    [Theory]
    [InlineData(QualityStatus.Pass, QualityStatus.Pass, true)]
    [InlineData(QualityStatus.Pass, QualityStatus.Warn, true)]
    [InlineData(QualityStatus.Pass, QualityStatus.Fail, false)]
    [InlineData(QualityStatus.Pass, QualityStatus.Unavailable, false)]
    [InlineData(QualityStatus.Warn, QualityStatus.Pass, false)]
    public void A_rolling_key_session_is_usable_when_coverage_passes_and_stitching_passes_or_warns(QualityStatus coverage, QualityStatus stitching, bool usable)
    {
        SessionUsability.IsUsable("rolling", [Check("coverage", coverage), Check("stitching", stitching)]).ShouldBe(usable);
    }

    [Fact]
    public void A_missing_check_makes_the_session_unusable()
    {
        SessionUsability.IsUsable("chain", [Check("coverage", QualityStatus.Pass)]).ShouldBeFalse();
        SessionUsability.IsUsable("chain", []).ShouldBeFalse();
    }
}
