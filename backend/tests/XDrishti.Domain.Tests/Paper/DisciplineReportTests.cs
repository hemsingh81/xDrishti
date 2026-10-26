using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Paper;

/// <summary>
/// Spec P10-03 on the hand-worked fixture: T1 to T6 followed (+2.0, -1.0, -1.0, +0.5, -1.0, +1.5), T7 sized down 100 to 60 (+2.0), T8 rejected (grade +1.5), T9 rejected (not triggered), T10 lapsed (-1.0), then T11 (approved, never
/// triggered) and T12 (approved, armed, capture not yet run). Every expected number is worked by hand, not computed by the code under test.
/// </summary>
public sealed class DisciplineReportTests
{
    private static readonly Guid A = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a04");

    private static readonly Guid B = Guid.Parse("1c7f3b0f-8b63-4e8f-8d4b-6e1b8f1c8b15");

    private static readonly DateOnly Day = new(2026, 11, 3);

    private static DateTimeOffset At(int hour) => new(Day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromMinutes(330));

    private static TicketGrade Graded(double r) => new(GradeStatus.Graded, r, null, null, "stop", null, null, "gross, no charges", string.Empty);

    private static TicketGrade Status(GradeStatus status) => new(status, null, null, null, null, null, null, "gross, no charges", string.Empty);

    private static ReviewTicket Ticket(long id, TicketState state, TicketGrade? grade = null, string? approval = "owner", int approved = 100, TicketGroup group = TicketGroup.Primary, bool confirmed = false, params Guid[] accounts) =>
        new(1, id, Day, group, "S" + id, "BUY", state, approval, confirmed, grade, (accounts.Length == 0 ? [A] : accounts).Select(a => new ReviewAllocation(a, 100, approved)).ToList());

    private static ReviewTrade Trade(long id, double r, int qty = 100, Guid? account = null, bool open = false, double exact = 0, double gross = 0, double? net = null, decimal result = 0m) =>
        new(1, id, account ?? A, "S" + id, "BUY", "primary", "key" + id, true, 100, qty, At(10),
            open ? null : new ReviewExit(At(14), "stop", result == 0m ? (decimal)r * 100m : result, null, net is null ? null : result, exact == 0 ? r : exact, gross == 0 ? r : gross, net, r));

    private static readonly double[] Results = [2.0, -1.0, -1.0, 0.5, -1.0, 1.5];

    private static (List<ReviewTicket> Tickets, List<ReviewTrade> Trades, List<ReviewSkip> Skips) Block1()
    {
        var tickets = new List<ReviewTicket>();
        var trades = new List<ReviewTrade>();
        for (var i = 0; i < 6; i++)
        {
            tickets.Add(Ticket(i + 1, TicketState.Closed));
            trades.Add(Trade(i + 1, Results[i]));
        }

        tickets.Add(Ticket(7, TicketState.Closed, approved: 60));
        trades.Add(Trade(7, 2.0, qty: 60));
        tickets.Add(Ticket(8, TicketState.Rejected, Graded(1.5), approval: null));
        tickets.Add(Ticket(9, TicketState.Rejected, Status(GradeStatus.NotTriggered), approval: null));
        tickets.Add(Ticket(10, TicketState.Lapsed, Graded(-1.0), approval: null));
        return (tickets, trades, []);
    }

    private static (List<ReviewTicket> Tickets, List<ReviewTrade> Trades, List<ReviewSkip> Skips) Block2()
    {
        var (tickets, trades, skips) = Block1();
        tickets.Add(Ticket(11, TicketState.Expired));
        skips.Add(new ReviewSkip(11, A, SkipCodes.NotTriggered));
        tickets.Add(Ticket(12, TicketState.Armed));
        return (tickets, trades, skips);
    }

    private static DisciplineLine Line(DisciplineReport report, string id) => report.Lines.Single(l => l.Id == id);

    [Fact]
    public void Block_one_counts_and_R_equal_the_hand_numbers()
    {
        var (tickets, trades, skips) = Block1();

        var report = DisciplineReport.Build(tickets, trades, skips, [], null);

        report.PrimaryCount.ShouldBe(10);
        Counts(report).ShouldBe(new Dictionary<string, int> { ["followed"] = 6, ["sized_down"] = 1, ["approved_not_entered"] = 0, ["pending"] = 0, ["rejected"] = 2, ["lapsed"] = 1, ["invalidated_preopen"] = 0 });
        Line(report, "followed").SumR.Number!.Value.ShouldBe(1.0, 1e-9);
        Line(report, "followed").Note.ShouldBe("3 wins of 6");
        Line(report, "sized_down").SumR.Number!.Value.ShouldBe(2.0, 1e-9);

        var skipped = Line(report, "skipped_graded");
        skipped.N.ShouldBe(2);
        skipped.SumR.Number!.Value.ShouldBe(0.5, 1e-9); // +1.5 - 1.0
        skipped.MeanR.State.ShouldBe(MetricState.Insufficient);
        skipped.MeanR.Reason.ShouldBe("n 2 < 15");
        skipped.Note.ShouldContain("1 not triggered");
        Line(report, "rejected").Note.ShouldContain("not_triggered 1");

        var size = Line(report, "size_effect");
        size.N.ShouldBe(1);
        size.SumR.Number!.Value.ShouldBe(0.8, 1e-9); // (100 - 60) / 100 x 2.0

        var notTaken = Line(report, "r_not_taken");
        notTaken.Title.ShouldBe("R not taken (positive: leaving it cost you; negative: it saved you)");
        notTaken.N.ShouldBe(3);
        notTaken.SumR.Number!.Value.ShouldBe(1.3, 1e-9);
        notTaken.MeanR.State.ShouldBe(MetricState.Insufficient);

        var taken = Line(report, "taken");
        taken.N.ShouldBe(7);
        taken.SumR.Number!.Value.ShouldBe(3.0, 1e-9);
    }

    [Fact]
    public void Block_two_adds_a_not_entered_and_a_pending_ticket_and_the_seven_sum_to_the_primary_count()
    {
        var (tickets, trades, skips) = Block2();

        var report = DisciplineReport.Build(tickets, trades, skips, [], null);

        report.PrimaryCount.ShouldBe(12);
        Counts(report).ShouldBe(new Dictionary<string, int> { ["followed"] = 6, ["sized_down"] = 1, ["approved_not_entered"] = 1, ["pending"] = 1, ["rejected"] = 2, ["lapsed"] = 1, ["invalidated_preopen"] = 0 });
        Counts(report).Values.Sum().ShouldBe(report.PrimaryCount);
        Line(report, "approved_not_entered").Note.ShouldBe("not_triggered 1");
        Line(report, "pending").Note.ShouldBe("awaiting_capture 1");
    }

    [Fact]
    public void Permuting_the_input_does_not_change_the_report()
    {
        var (tickets, trades, skips) = Block2();
        var expected = DisciplineReport.Build(tickets, trades, skips, [], null).Lines.Select(PaperReviewBuilder.Text).ToList();

        var shuffled = DisciplineReport.Build([.. tickets.AsEnumerable().Reverse()], [.. trades.AsEnumerable().Reverse()], [.. skips.AsEnumerable().Reverse()], [], null).Lines.Select(PaperReviewBuilder.Text).ToList();

        shuffled.ShouldBe(expected);
    }

    [Fact]
    public void Every_ticket_state_and_trail_shape_falls_in_exactly_one_of_the_seven()
    {
        foreach (var state in Enum.GetValues<TicketState>())
        {
            foreach (var shape in new[] { "none", "open", "closed", "skipped", "reduced_closed" })
            {
                var t = Ticket(1, state, approved: shape == "reduced_closed" ? 50 : 100);
                var trades = shape switch
                {
                    "open" => new List<ReviewTrade> { Trade(1, 0, open: true) },
                    "closed" => [Trade(1, 1.0)],
                    "reduced_closed" => [Trade(1, 1.0, qty: 50)],
                    _ => [],
                };
                var skips = shape == "skipped" ? new List<ReviewSkip> { new(1, A, SkipCodes.NotGradable) } : [];

                var outcome = DisciplineClassifier.Classify(t, trades, skips);

                DisciplineCategory.All.Count(c => c == outcome.Category).ShouldBe(1, $"{state}/{shape}");
            }
        }
    }

    [Theory]
    [InlineData(TicketState.Rejected, "rejected")]
    [InlineData(TicketState.Lapsed, "lapsed")]
    [InlineData(TicketState.Invalidated, "invalidated_preopen")]
    [InlineData(TicketState.Proposed, "pending")]
    [InlineData(TicketState.Modified, "pending")]
    public void Decision_states_decide_the_category_whatever_the_paper_record_says(TicketState state, string category)
    {
        DisciplineClassifier.Classify(Ticket(1, state), [], []).Category.ShouldBe(category);
    }

    [Fact]
    public void A_ticket_sized_for_two_accounts_is_one_ticket_one_R_and_two_rupee_lines()
    {
        var tickets = new List<ReviewTicket> { Ticket(1, TicketState.Closed, accounts: [A, B]) };
        var trades = new List<ReviewTrade> { Trade(1, 2.0, account: A, result: 200m), Trade(1, 2.0, account: B, result: 150m) };

        var report = DisciplineReport.Build(tickets, trades, [], [], null);

        report.PrimaryCount.ShouldBe(1);
        Line(report, "followed").N.ShouldBe(1);
        Line(report, "followed").SumR.Number!.Value.ShouldBe(2.0, 1e-9);
        Line(report, "followed").Rupees.Select(r => (r.AccountId, r.Amount)).ShouldBe([(A, 200m), (B, 150m)]);
        Line(report, "taken").N.ShouldBe(1);
    }

    [Fact]
    public void A_mean_needs_fifteen_graded_tickets_and_is_an_indication_below_thirty()
    {
        DisciplineReport.Mean([.. Enumerable.Repeat(1.0, 14)]).State.ShouldBe(MetricState.Insufficient);
        var fifteen = DisciplineReport.Mean([.. Enumerable.Repeat(1.0, 15)]);
        fifteen.State.ShouldBe(MetricState.Provisional);
        fifteen.Number!.Value.ShouldBe(1.0);
        DisciplineReport.Mean([.. Enumerable.Repeat(1.0, 30)]).State.ShouldBe(MetricState.Value);
        DisciplineReport.Mean([]).State.ShouldBe(MetricState.Empty);
    }

    [Fact]
    public void Not_triggered_and_missing_grades_are_counted_apart_and_never_R_zero()
    {
        var tickets = new List<ReviewTicket>
        {
            Ticket(1, TicketState.Rejected, Status(GradeStatus.NotTriggered), approval: null),
            Ticket(2, TicketState.Rejected, Status(GradeStatus.NotGradable), approval: null),
            Ticket(3, TicketState.Lapsed, null, approval: null),
        };

        var line = Line(DisciplineReport.Build(tickets, [], [], [], null), "skipped_graded");

        line.N.ShouldBe(0);
        line.SumR.State.ShouldBe(MetricState.Empty);
        line.Note.ShouldContain("1 not triggered and 2 not graded");
    }

    [Fact]
    public void Size_effect_of_an_open_or_not_entered_size_down_adds_nothing_and_is_counted_apart()
    {
        var tickets = new List<ReviewTicket> { Ticket(1, TicketState.Triggered, approved: 60), Ticket(2, TicketState.Expired, approved: 60) };
        var trades = new List<ReviewTrade> { Trade(1, 0, qty: 60, open: true) };
        var skips = new List<ReviewSkip> { new(2, A, SkipCodes.NotTriggered) };

        var line = Line(DisciplineReport.Build(tickets, trades, skips, [], null), "size_effect");

        line.N.ShouldBe(0);
        line.SumR.State.ShouldBe(MetricState.Empty);
        line.Note.ShouldStartWith("2 size-down(s) still open or never entered add nothing");
    }

    [Fact]
    public void Flags_reserve_and_auto_are_counted_beside_the_seven()
    {
        var tickets = new List<ReviewTicket>
        {
            Ticket(1, TicketState.Closed, confirmed: true),
            Ticket(2, TicketState.Closed, approval: "auto"),
            Ticket(3, TicketState.Closed, group: TicketGroup.Reserve),
            Ticket(4, TicketState.Lapsed, approval: null, group: TicketGroup.Reserve),
        };
        var trades = new List<ReviewTrade> { Trade(1, 1.0), Trade(2, 1.0) };

        var report = DisciplineReport.Build(tickets, trades, [], [], null);

        report.PrimaryCount.ShouldBe(2);
        Line(report, "confirmed_despite").N.ShouldBe(1);
        Line(report, "auto_approved").N.ShouldBe(1);
        Line(report, "reserve_approved").N.ShouldBe(1);
    }

    [Fact]
    public void Added_fills_are_counted_not_graded_and_empty_is_not_zero()
    {
        var none = Line(DisciplineReport.Build([], [], [], [], null), "added");
        none.SumR.State.ShouldBe(MetricState.Empty);
        none.Note.ShouldStartWith("empty");

        ReviewFill[] fills = [new(A, "AAA", true, 10, 100m, 5m), new(A, "AAA", false, 10, 110m, 5m), new(A, "BBB", true, 5, 50m, 1m)];
        var added = Line(DisciplineReport.Build([], [], [], fills, null), "added");

        added.N.ShouldBe(3);
        added.SumR.State.ShouldBe(MetricState.Unavailable);
        added.SumR.Reason.ShouldBe("no plan levels");
        added.Rupees.Single().Amount.ShouldBe(90m); // AAA round trip: 1100 - 1000 - 10; BBB is still open and adds nothing
        added.Note.ShouldContain("1 of 2 finished round trips");

        Line(DisciplineReport.Build([], [], [], null, "the journal could not be read"), "added").SumR.State.ShouldBe(MetricState.Unavailable);
    }

    [Fact]
    public void The_waterfall_for_one_trade_is_plan_slippage_charges_net()
    {
        // plan 2.0000, slippage -0.0455, charges -0.0455 (cost 5.00 on risk 110.00), net 1.9091
        var cost = 5.0 / 110.0;
        var exit = new ReviewExit(At(14), "target", 200m, 5m, 195m, 2.0, 2.0 - cost, 2.0 - 2 * cost, 2.0 - 2 * cost);

        var rows = ExecutionGap.Rows([exit]);

        rows.Select(r => r.Id).ShouldBe(["plan", "slippage", "charges", "net", "missed_triggers", "real_slippage"]);
        Math.Round(rows[0].Value.Number!.Value, 4).ShouldBe(2.0000);
        Math.Round(rows[1].Value.Number!.Value, 4).ShouldBe(-0.0455);
        Math.Round(rows[2].Value.Number!.Value, 4).ShouldBe(-0.0455);
        Math.Round(rows[3].Value.Number!.Value, 4).ShouldBe(1.9091);
        rows[4].Value.State.ShouldBe(MetricState.NotEvaluable);
        rows[4].Value.Reason.ShouldBe("paper has no real fills (P11)");
        rows[5].Value.State.ShouldBe(MetricState.NotEvaluable);
    }

    [Fact]
    public void Without_a_charge_sheet_charges_and_net_are_unavailable_and_with_part_of_one_they_are_provisional()
    {
        var gross = new ReviewExit(At(14), "stop", 100m, null, null, 1.0, 0.98, null, 0.98);
        var charged = new ReviewExit(At(14), "stop", 100m, 5m, 95m, 1.0, 0.98, 0.94, 0.94);

        ExecutionGap.Rows([gross])[2].Value.State.ShouldBe(MetricState.Unavailable);
        ExecutionGap.Rows([gross])[3].Value.State.ShouldBe(MetricState.Unavailable);
        ExecutionGap.Rows([gross, charged])[2].Value.State.ShouldBe(MetricState.Provisional);
        ExecutionGap.Rows([])[0].Value.State.ShouldBe(MetricState.Empty);
    }

    private static Dictionary<string, int> Counts(DisciplineReport report) => DisciplineCategory.All.ToDictionary(c => c, c => Line(report, c).N);
}
