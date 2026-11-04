using XDrishti.Domain.Options.Quality;

namespace XDrishti.Domain.Tests.Options.Quality;

public sealed class ArchiveSessionFiguresTests
{
    private static QualityCheck Check(string code, QualityStatus status, string? reason = null) => new(code, status, status == QualityStatus.Unavailable ? null : 0.9, 0.95, reason, 10, null);

    private static IReadOnlyList<QualityCheck> Day(QualityStatus coverage, QualityStatus parity, string? coverageReason = null, string? parityReason = null) =>
        [Check("coverage", coverage, coverageReason), Check("parity", parity, parityReason), Check("greeks", QualityStatus.Pass)];

    [Fact]
    public void Nothing_checked_has_no_sessions_dates_or_outcomes()
    {
        var figures = ArchiveSessionFigures.Of(new Dictionary<DateOnly, IReadOnlyList<QualityCheck>>());

        figures.Checked.ShouldBe(0);
        figures.Recorded.ShouldBe(0);
        figures.First.ShouldBeNull();
        figures.Last.ShouldBeNull();
        figures.Outcomes.ShouldBeEmpty();
    }

    [Fact]
    public void Recorded_usable_dates_and_outcomes_are_counted_and_a_not_recorded_day_is_checked_but_not_recorded()
    {
        var sessions = new Dictionary<DateOnly, IReadOnlyList<QualityCheck>>
        {
            [new DateOnly(2026, 10, 5)] = Day(QualityStatus.Unavailable, QualityStatus.Unavailable, "not_recorded", "not_recorded"),
            [new DateOnly(2026, 10, 6)] = Day(QualityStatus.Pass, QualityStatus.Pass),
            [new DateOnly(2026, 10, 7)] = Day(QualityStatus.Pass, QualityStatus.Warn),
            [new DateOnly(2026, 10, 8)] = Day(QualityStatus.Pass, QualityStatus.Unavailable, null, "too_few_points"),
            [new DateOnly(2026, 10, 9)] = Day(QualityStatus.Fail, QualityStatus.Pass),
        };

        var figures = ArchiveSessionFigures.Of(sessions);

        figures.Checked.ShouldBe(5);
        figures.Recorded.ShouldBe(4);
        figures.Usable.ShouldBe(2); // an unavailable parity never makes a session usable
        figures.First.ShouldBe(new DateOnly(2026, 10, 6));
        figures.Last.ShouldBe(new DateOnly(2026, 10, 9));
        figures.Outcomes.Select(o => (o.Outcome, o.Reason, o.Sessions)).ShouldBe([("pass", null, 1), ("warn", null, 1), ("fail", null, 1), ("unavailable", "not_recorded", 1), ("unavailable", "too_few_points", 1)]);
    }
}
