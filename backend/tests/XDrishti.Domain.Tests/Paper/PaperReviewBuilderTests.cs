using XDrishti.Domain.Paper;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Paper;

/// <summary>Spec P10-03 criteria 8 to 11: the review text is deterministic and pinned, the ETA rule is honest, the storing window is the digest's and a missing source shows as unavailable while the rest is produced.</summary>
public sealed class PaperReviewBuilderTests
{
    private static readonly Guid A = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a04");

    private static readonly PaperReviewWeek Week = PaperReviewWeek.TryParse("2026-W48")!.Value; // 2026-11-23 to 2026-11-29

    private static readonly DateTimeOffset Saturday = new(2026, 11, 28, 10, 30, 0, TimeSpan.FromMinutes(330));

    private static readonly ReviewEpoch Epoch = new(7, new DateOnly(2026, 11, 2), "first", "the default epoch");

    private static DateTimeOffset At(DateOnly day, int hour) => new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromMinutes(330));

    private static ReviewTrade Closed(int n, DateOnly exitDay) =>
        new(1, n, A, "S" + n, "BUY", "primary", "key" + n, true, 10, 10, At(exitDay, 10), new ReviewExit(At(exitDay, 14), "target", 100m, null, null, 1.0, 1.0, null, 1.0));

    private static PaperReviewInput Input(int counted, PaperReviewWeek? week = null, DateTimeOffset? at = null, PaperRecordFacts? record = null, bool withRecord = true)
    {
        var trades = Enumerable.Range(1, counted).Select(i => Closed(i, new DateOnly(2026, 11, 24))).ToList();
        var facts = record ?? new PaperRecordFacts([], trades, [], [], null);
        return new PaperReviewInput(week ?? Week, at ?? Saturday, Epoch, new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 27), 5, 5, withRecord ? facts : null, "the paper record could not be read (TimeoutException)", new ReviewConfig([], 0, 2), null, "unavailable: the backtest comparison (P10-04) is not built");
    }

    private static ReviewMetric Eta(PaperReview review) => review.Sections.Single(s => s.Id == "standing").Metrics.Single(m => m.Id == "eta_weeks");

    [Fact]
    public void The_text_opens_with_the_two_pinned_lines_and_every_section_ends_with_its_limits()
    {
        var review = PaperReviewBuilder.Build(Input(3));

        var lines = review.Text.Split('\n');
        lines[0].ShouldBe("Paper only: no order was placed and none exists.");
        lines[1].ShouldBe("Nothing was changed: this review applies nothing; changing any setting is your own act.");
        review.Sections.Select(s => s.Id).ShouldBe(["standing", "week", "discipline", "execution", "config", "comparison"]);
        foreach (var section in review.Sections)
        {
            review.Text.ShouldContain("Limits: " + section.Limits + "\n");
            section.Limits.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void The_same_stored_records_give_the_same_text_byte_for_byte()
    {
        PaperReviewBuilder.Build(Input(12)).Text.ShouldBe(PaperReviewBuilder.Build(Input(12)).Text);
    }

    [Fact]
    public void With_no_epoch_the_review_says_so_and_nothing_else()
    {
        var review = PaperReviewBuilder.Build(Input(0) with { Epoch = null });

        review.Text.ShouldBe(
            "Paper only: no order was placed and none exists.\n"
            + "Nothing was changed: this review applies nothing; changing any setting is your own act.\n"
            + "Weekly paper review 2026-W48 (2026-11-23 to 2026-11-29)\n"
            + "Built at 2026-11-28 10:30 IST.\n"
            + "\n1. No paper epoch\n"
            + "No paper epoch exists yet: nothing was captured, so there is nothing to review.\n"
            + "Limits: A review needs the first paper capture to have run.\n");
        review.Sections.Count.ShouldBe(1);
    }

    [Fact]
    public void A_week_built_after_it_ended_says_the_state_is_the_build_dates()
    {
        var late = new DateTimeOffset(2026, 12, 2, 9, 0, 0, TimeSpan.FromMinutes(330));

        PaperReviewBuilder.Build(Input(3, at: late)).Text.ShouldContain("State as of 2026-12-02, not as of the end of the week");
        PaperReviewBuilder.Build(Input(3)).Text.ShouldNotContain("State as of");
    }

    [Fact]
    public void An_ETA_needs_ten_counted_trades_and_three_weeks_and_is_a_straight_line()
    {
        // epoch start 2026-11-02, as of 2026-11-28: day 27, 3.857 weeks
        var nine = Eta(PaperReviewBuilder.Build(Input(9)));
        nine.State.ShouldBe("unavailable");
        nine.Number.ShouldBeNull();

        var ten = Eta(PaperReviewBuilder.Build(Input(10)));
        ten.State.ShouldBe("value");
        ten.Number!.Value.ShouldBe(50.0 / (10 / (27 / 7.0)), 1e-9);
        PaperReviewBuilder.Build(Input(10)).Text.ShouldContain("A straight-line guess.");

        var early = Input(10) with { Epoch = Epoch with { StartedOn = new DateOnly(2026, 11, 15) } };
        Eta(PaperReviewBuilder.Build(early)).State.ShouldBe("unavailable");
    }

    [Fact]
    public void Counted_trades_are_closed_primary_measured_and_once_per_ticket()
    {
        var day = new DateOnly(2026, 11, 24);
        var a = Closed(1, day);
        var twin = a with { AccountId = Guid.Parse("1c7f3b0f-8b63-4e8f-8d4b-6e1b8f1c8b15") };
        ReviewTrade[] trades = [a, twin, Closed(2, day) with { Measured = false }, Closed(3, day) with { TicketGroup = "reserve" }, Closed(4, day) with { Exit = null }, Closed(5, day)];

        PaperReviewBuilder.Counted(trades).Select(t => t.ItemId).ShouldBe([1, 5]);
    }

    [Fact]
    public void An_unreadable_paper_record_marks_those_sections_unavailable_and_the_rest_is_still_produced()
    {
        var review = PaperReviewBuilder.Build(Input(3, withRecord: false));

        foreach (var id in new[] { "standing", "week", "discipline", "execution" })
        {
            review.Sections.Single(s => s.Id == id).Lines.ShouldBe(["unavailable: the paper record could not be read (TimeoutException)"]);
        }

        review.Sections.Single(s => s.Id == "config").Lines.ShouldContain(l => l.StartsWith("No new epoch started", StringComparison.Ordinal));
        review.Sections.Single(s => s.Id == "comparison").Lines.Single().ShouldStartWith("unavailable:");
        review.Discipline.ShouldBeNull();
    }

    [Fact]
    public void No_trade_yet_shows_empty_words_never_zeros()
    {
        var review = PaperReviewBuilder.Build(Input(0));

        var discipline = review.Sections.Single(s => s.Id == "discipline");
        discipline.Metrics.Where(m => m.Id.EndsWith("_mean", StringComparison.Ordinal)).ShouldAllBe(m => m.State != "value");
        review.Sections.Single(s => s.Id == "week").Lines.ShouldContain("Closed paper trades this window: none.");
        review.Sections.Single(s => s.Id == "execution").Metrics.Single(m => m.Id == "plan").State.ShouldBe("empty");
    }

    [Fact]
    public void The_closed_trades_table_lists_one_row_per_account_with_the_week_trades_only()
    {
        var inside = Closed(1, new DateOnly(2026, 11, 24));
        var outside = Closed(2, new DateOnly(2026, 11, 10));
        var record = new PaperRecordFacts([], [inside, outside], [], [], null);

        var table = PaperReviewBuilder.Build(Input(0, record: record)).Sections.Single(s => s.Id == "week").Table!;

        table.Rows.Count.ShouldBe(1);
        table.Rows[0][1].ShouldBe("S1");
        table.Rows[0].Count.ShouldBe(table.Columns.Count);
    }

    [Theory]
    [InlineData("2026-W48", "2026-11-28T10:30:00+05:30", false)] // current week, Saturday 10:30
    [InlineData("2026-W48", "2026-11-28T09:59:00+05:30", true)] // before Saturday 10:00
    [InlineData("2026-W48", "2026-11-30T09:00:00+05:30", false)] // most recent completed week
    [InlineData("2026-W47", "2026-11-30T09:00:00+05:30", true)] // older: a dry run only
    public void Storing_is_allowed_only_for_the_current_week_from_saturday_or_the_most_recent_completed_week(string week, string now, bool refused)
    {
        (PaperReviewWeek.TryParse(week)!.Value.StoreRefusal(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)) is not null).ShouldBe(refused);
    }

    [Fact]
    public void A_future_week_is_future_and_a_malformed_week_does_not_parse()
    {
        PaperReviewWeek.TryParse("2026-W49")!.Value.IsFuture(Saturday).ShouldBeTrue();
        Week.IsFuture(Saturday).ShouldBeFalse();
        PaperReviewWeek.TryParse("2026-49").ShouldBeNull();
        PaperReviewWeek.TryParse("2026-W60").ShouldBeNull();
    }

    [Fact]
    public void A_non_value_metric_is_written_as_its_state_and_reason()
    {
        ReviewText.R(Metric.Insufficient("n 2 < 15")).ShouldBe("insufficient: n 2 < 15");
        ReviewText.R(Metric.Of(0.5)).ShouldBe("+0.50R");
        ReviewText.R(Metric.Of(-1.25)).ShouldBe("-1.25R");
    }
}
