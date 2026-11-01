using XDrishti.Domain.Options.Quality;

namespace XDrishti.Domain.Tests.Options.Quality;

public sealed class QualityStatusesTests
{
    [Theory]
    [InlineData(QualityStatus.Pass, QualityStatus.Warn, QualityStatus.Warn)]
    [InlineData(QualityStatus.Warn, QualityStatus.Unavailable, QualityStatus.Unavailable)]
    [InlineData(QualityStatus.Unavailable, QualityStatus.Fail, QualityStatus.Fail)]
    [InlineData(QualityStatus.Pass, QualityStatus.Unavailable, QualityStatus.Unavailable)]
    [InlineData(QualityStatus.Fail, QualityStatus.Pass, QualityStatus.Fail)]
    public void The_roll_up_is_fail_above_unavailable_above_warn_above_pass_in_either_order(QualityStatus a, QualityStatus b, QualityStatus expected)
    {
        QualityStatuses.Worst([a, b]).ShouldBe(expected);
        QualityStatuses.Worst([b, a]).ShouldBe(expected);
    }

    [Fact]
    public void Nothing_to_roll_up_is_unavailable_and_never_pass()
    {
        QualityStatuses.Worst([]).ShouldBe(QualityStatus.Unavailable);
    }

    [Fact]
    public void Unavailable_among_passes_is_not_a_pass()
    {
        QualityStatuses.Worst([QualityStatus.Pass, QualityStatus.Pass, QualityStatus.Unavailable]).ShouldNotBe(QualityStatus.Pass);
    }

    [Theory]
    [InlineData(QualityStatus.Pass, "pass")]
    [InlineData(QualityStatus.Warn, "warn")]
    [InlineData(QualityStatus.Fail, "fail")]
    [InlineData(QualityStatus.Unavailable, "unavailable")]
    public void The_stored_words_round_trip(QualityStatus status, string word)
    {
        status.Code().ShouldBe(word);
        QualityStatuses.TryParse(word, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(status);
    }

    [Fact]
    public void An_unknown_word_does_not_parse()
    {
        QualityStatuses.TryParse("good", out _).ShouldBeFalse();
    }
}
