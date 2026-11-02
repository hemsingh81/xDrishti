using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>Spec P15-03 criteria 6 and 7: the per-basis read at an instant, the hand example, the visibility property and the explicit unavailable.</summary>
public sealed class ResultFactsAsOfRuleTests
{
    private static readonly Guid Instrument = new("11111111-0000-0000-0000-000000000001");
    private static readonly DateOnly Q1 = new(2026, 6, 30);
    private static readonly TimeSpan IndiaOffset = TimeSpan.FromMinutes(330);
    private static readonly TimeSpan Lag = TimeSpan.FromMinutes(5);

    private static DateTimeOffset Ist(int month, int day, int hour, int minute) => new DateTimeOffset(2026, month, day, hour, minute, 0, IndiaOffset).ToUniversalTime();

    private static ResultFilingFacts Filing(int seed, DateTimeOffset? ts, string basis, decimal revenue, DateOnly? period = null, string span = "q", bool undated = false)
    {
        var id = new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);
        var stored = new StoredFiling(id, Instrument, FilingKind.Results, period ?? Q1, undated ? null : ts, undated ? BroadcastState.Unavailable : BroadcastState.Ok, undated ? "missing" : null, ts ?? Ist(11, 2, 12, 0), new string('a', 64), 10, "f.xml", null);
        return new ResultFilingFacts(stored, [new StoredResultFact("revenue", span, basis, revenue, "inr", null), new StoredResultFact("pat", span, basis, null, "inr", "not_in_file")]);
    }

    private static readonly ResultFilingFacts Standalone = Filing(1, Ist(7, 24, 16, 12), "standalone", 3000);
    private static readonly ResultFilingFacts Consolidated = Filing(2, Ist(7, 24, 16, 40), "consolidated", 5000);
    private static readonly ResultFilingFacts Restatement = Filing(3, Ist(8, 30, 10, 0), "consolidated", 5100);

    private static ResultFactsAnswer At(DateTimeOffset t, params ResultFilingFacts[] rows) => ResultFactsAsOfRule.Compute(rows, t, Lag);

    [Fact]
    public void The_hand_example_standalone_alone_then_both_then_the_restated_consolidated_beside_the_original_standalone()
    {
        ResultFilingFacts[] all = [Standalone, Consolidated, Restatement];

        var early = At(Ist(7, 24, 16, 30), all);
        var both = At(Ist(7, 24, 16, 45), all);
        var late = At(Ist(8, 30, 10, 5), all);

        early.Facts.Where(f => f.Field == "revenue").Select(f => (f.Basis, f.Value)).ShouldBe([("standalone", 3000m)]);
        both.Facts.Where(f => f.Field == "revenue").Select(f => (f.Basis, f.Value)).ShouldBe([("consolidated", 5000m), ("standalone", 3000m)]);
        late.Facts.Where(f => f.Field == "revenue").Select(f => (f.Basis, f.Value, f.FilingId)).ShouldBe([("consolidated", 5100m, Restatement.Filing.Id), ("standalone", 3000m, Standalone.Filing.Id)]);
    }

    [Fact]
    public void The_lag_applies_and_the_boundary_is_inclusive()
    {
        var broadcast = Ist(7, 24, 16, 12);

        At(broadcast.AddMinutes(4), Standalone).Facts.ShouldBeEmpty();
        At(broadcast.Add(Lag), Standalone).Facts.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_restatement_replaces_only_what_it_restates_and_only_from_its_own_time()
    {
        var restatedYtd = Filing(4, Ist(8, 30, 10, 0), "consolidated", 7777, span: "ytd");

        var answer = At(Ist(9, 1, 0, 0), Consolidated, restatedYtd);

        answer.Get(Q1, "revenue", "q", "consolidated").Value.ShouldBe(5000m);
        answer.Get(Q1, "revenue", "ytd", "consolidated").Value.ShouldBe(7777m);
    }

    [Fact]
    public void A_field_never_seen_is_unavailable_not_in_file_and_never_zero_and_a_stored_unavailable_keeps_its_reason()
    {
        var answer = At(Ist(8, 1, 0, 0), Consolidated);

        var stored = answer.Get(Q1, "pat", "q", "consolidated");
        var never = answer.Get(Q1, "eps_basic", "q", "consolidated");
        var otherBasis = answer.Get(Q1, "revenue", "q", "standalone");

        (stored.State, stored.Value, stored.Reason, stored.FilingId).ShouldBe(("unavailable", null, "not_in_file", Consolidated.Filing.Id));
        (never.State, never.Value, never.Reason, never.FilingId).ShouldBe(("unavailable", null, "not_in_file", null));
        otherBasis.State.ShouldBe("unavailable");
    }

    [Fact]
    public void An_undated_envelopes_facts_are_never_returned_only_counted()
    {
        var undated = Filing(5, null, "consolidated", 999, undated: true);

        var answer = At(Ist(11, 2, 12, 0), undated);

        answer.Facts.ShouldBeEmpty();
        answer.UndatedCount.ShouldBe(1);
    }

    [Fact]
    public void Adding_changing_or_removing_a_filing_not_visible_at_t_leaves_the_answer_byte_identical_and_a_visible_one_changes_it()
    {
        var t = Ist(7, 24, 16, 45);
        string Render(params ResultFilingFacts[] rows) => string.Join("|", At(t, rows).Facts.Select(f => $"{f.PeriodEnd}/{f.Field}/{f.Span}/{f.Basis}/{f.State}/{f.Value}/{f.Reason}/{f.FilingId}/{f.BroadcastTs:O}"));
        var baseline = Render(Standalone, Consolidated);

        var future = Filing(6, Ist(8, 30, 10, 0), "consolidated", 1);
        var futureChanged = Filing(6, Ist(8, 30, 10, 0), "consolidated", 2);
        var futureNewPeriod = Filing(7, Ist(9, 1, 10, 0), "standalone", 4, new DateOnly(2026, 9, 30));

        Render(Standalone, Consolidated, future).ShouldBe(baseline);
        Render(Standalone, Consolidated, futureChanged).ShouldBe(baseline);
        Render(Standalone, Consolidated, futureNewPeriod).ShouldBe(baseline);
        Render(Consolidated, Standalone).ShouldBe(baseline, "the insert order changes nothing");
        Render(Standalone).ShouldNotBe(baseline, "the control: a visible filing changes the answer");
        Render(Standalone, Filing(2, Ist(7, 24, 16, 40), "consolidated", 5001)).ShouldNotBe(baseline, "the control: a visible fact changes the answer");
    }

    [Fact]
    public void Every_order_of_the_filings_gives_the_same_answer()
    {
        ResultFilingFacts[] all = [Standalone, Consolidated, Restatement, Filing(8, Ist(8, 30, 10, 0), "standalone", 2900)];
        var t = Ist(9, 1, 0, 0);
        var expected = At(t, all).Facts;

        foreach (var order in new[] { all.Reverse().ToArray(), [all[2], all[0], all[3], all[1]], [all[3], all[2], all[1], all[0]] })
        {
            At(t, order).Facts.ShouldBe(expected);
        }
    }

    [Fact]
    public void A_tie_in_time_falls_to_the_lower_filing_id()
    {
        var a = Filing(10, Ist(8, 1, 10, 0), "consolidated", 1);
        var b = Filing(11, Ist(8, 1, 10, 0), "consolidated", 2);

        At(Ist(8, 2, 0, 0), b, a).Get(Q1, "revenue", "q", "consolidated").FilingId.ShouldBe(a.Filing.Id);
    }
}
