using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>BK-10b: which loosenings a plan shows (pure rule): at or before the plan was built, oldest first, and the sentence says what changed.</summary>
public sealed class LooseningNoticesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 6, 0, 0, TimeSpan.Zero);

    private static LooseningNotice Notice(string source, long id, DateTimeOffset at) => new(source, id, "k", null, "1", "2", "owner:a@b", at);

    [Fact]
    public void A_plan_shows_what_changed_at_or_before_it_was_built_oldest_first_with_ties_by_source_then_id()
    {
        var shown = LooseningNotices.Shown([Notice("sizing", 9, T0.AddHours(3)), Notice("sizing", 2, T0), Notice("basket", 5, T0), Notice("budget", 1, T0.AddHours(1)), Notice("sizing", 3, T0.AddHours(1))], T0.AddHours(1));

        shown.Select(n => n.NoticeId).ShouldBe(["basket:5", "sizing:2", "budget:1", "sizing:3"]);
    }

    [Fact]
    public void The_sentence_names_the_india_date_the_key_the_old_and_new_value_and_the_actor()
    {
        var notice = new LooseningNotice("sizing", 1, "sizing.capacity.pct", null, "0.003", "0.005", "owner:me@x", new DateTimeOffset(2026, 11, 10, 20, 0, 0, TimeSpan.Zero));

        notice.Sentence.ShouldBe("Loosened since 2026-11-11: sizing.capacity.pct 0.003 → 0.005 (by owner:me@x).");
        notice.NoticeId.ShouldBe("sizing:1");
    }

    [Fact]
    public void Nothing_shows_before_the_first_change_and_nothing_is_dismissed_automatically_with_the_setting_off()
    {
        LooseningNotices.Shown([Notice("sizing", 1, T0)], T0.AddSeconds(-1)).ShouldBeEmpty();
        LooseningNotices.AutoDismissible([Notice("sizing", 1, T0)], false, T0.AddDays(1)).ShouldBeEmpty();
    }
}
