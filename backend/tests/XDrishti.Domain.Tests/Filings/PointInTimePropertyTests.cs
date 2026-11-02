using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>
/// Spec P15-02 criterion 8 (the guard of the point-in-time rule): random sets of filings (kinds, periods, restatements, out-of-order filings, undated filings, filings without a period) and a random instant, at least 200 generated cases per property, fixed seed.
/// (a) a filing broadcast too late to be known, or one with no time, cannot change what <see cref="PointInTimeRule.AsOf"/> lists; (b) control: changing a visible current filing does change it; (c) a restatement after t is invisible and one before t hides its
/// predecessor; (d) a later instant never un-sees a filing that no later visible filing replaced; (e) the order of the rows and the ingest times do not matter.
/// </summary>
public sealed class PointInTimePropertyTests
{
    private const int Cases = 250;

    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly int[] Lags = [0, 5, 5, 30, 60];

    private sealed class World
    {
        public required List<StoredFiling> Rows { get; init; }

        public required TimeSpan Lag { get; init; }

        public required DateTimeOffset T { get; init; }

        public DateTimeOffset Cutoff => T - Lag;
    }

    private static World Generate(Random random)
    {
        var rows = new List<StoredFiling>();
        var n = 0;
        var lag = TimeSpan.FromMinutes(Lags[random.Next(Lags.Length)]);
        var periods = random.Next(1, 6);
        for (var p = 0; p < periods; p++)
        {
            var period = new DateOnly(2025, 3, 31).AddMonths(3 * p);
            var time = Origin.AddMinutes(random.Next(0, 6000));
            StoredFiling? previous = null;
            var chain = random.Next(1, 4);
            var used = new HashSet<DateTimeOffset>();
            for (var c = 0; c < chain; c++)
            {
                time = time.AddMinutes(random.Next(1, 2500));
                used.Add(time);
                var row = FilingSamples.Ok(++n, period, time, previous?.Id, ingested: Origin.AddMinutes(random.Next(0, 20000)), bytes: random.Next(1, 1000));
                rows.Add(row);
                previous = row;
            }

            if (random.Next(4) == 0)
            {
                var early = Origin.AddMinutes(random.Next(0, 12000));
                if (used.Add(early))
                {
                    rows.Add(FilingSamples.Ok(++n, period, early, ingested: Origin.AddMinutes(random.Next(0, 20000))));
                }
            }

            if (random.Next(3) == 0)
            {
                rows.Add(FilingSamples.Undated(++n, period));
            }
        }

        var standalone = random.Next(0, 3);
        for (var s = 0; s < standalone; s++)
        {
            rows.Add(FilingSamples.Ok(++n, null, Origin.AddMinutes(random.Next(0, 12000)), kind: FilingKind.Results));
        }

        return new World { Rows = rows, Lag = lag, T = Origin.AddMinutes(random.Next(0, 14000)).AddSeconds(random.Next(0, 60)) };
    }

    private static bool Future(StoredFiling row, World w) => row.BroadcastTs is { } ts && ts + w.Lag > w.T;

    private static string Answer(IEnumerable<StoredFiling> rows, World w, bool counts = true) => FilingSamples.Serialize(PointInTimeRule.AsOf(rows, w.T, w.Lag), counts);

    [Fact]
    public void A_filing_broadcast_too_late_to_be_known_never_changes_the_answer()
    {
        var random = new Random(20261102);
        var changed = 0;
        for (var i = 0; i < Cases; i++)
        {
            var w = Generate(random);
            var baseline = Answer(w.Rows, w);
            var next = 1000;

            // add: a new future filing in a new period, a future revision of a group's head, a future filing without a period
            var added = new List<StoredFiling>(w.Rows)
            {
                FilingSamples.Ok(++next, new DateOnly(2030, 3, 31), w.Cutoff.AddMinutes(random.Next(1, 5000))),
                FilingSamples.Ok(++next, null, w.Cutoff.AddMinutes(random.Next(1, 5000))),
            };
            var head = w.Rows.Where(r => r.PeriodEnd is not null && r.BroadcastTs is not null).OrderByDescending(r => r.BroadcastTs).FirstOrDefault();
            if (head is not null)
            {
                var when = new[] { w.Cutoff, head.BroadcastTs!.Value }.Max().AddMinutes(random.Next(1, 5000));
                added.Add(FilingSamples.Ok(++next, head.PeriodEnd, when, head.Id));
            }

            Answer(added, w).ShouldBe(baseline);

            // change: shift every future filing later and rewrite its other fields
            var rewritten = w.Rows.Select(r => Future(r, w) ? r with { BroadcastTs = r.BroadcastTs!.Value.AddMinutes(random.Next(1, 3000)), FileBytes = r.FileBytes + 7, FileName = "x" + r.FileName, IngestedAt = Origin } : r).ToList();
            Answer(rewritten, w).ShouldBe(baseline);

            // remove: every future filing
            var removed = w.Rows.Where(r => !Future(r, w)).ToList();
            Answer(removed, w).ShouldBe(baseline);

            changed += w.Rows.Any(r => Future(r, w)) ? 1 : 0;
        }

        changed.ShouldBeGreaterThan(Cases / 4); // the generator does produce future filings
    }

    [Fact]
    public void A_filing_with_no_time_never_changes_the_listed_filings()
    {
        var random = new Random(20261103);
        for (var i = 0; i < Cases; i++)
        {
            var w = Generate(random);
            var baseline = Answer(w.Rows, w, counts: false);
            var next = 2000;

            var added = new List<StoredFiling>(w.Rows) { FilingSamples.Undated(++next, new DateOnly(2025, 3, 31)), FilingSamples.Undated(++next, null, "no_offset") };
            var rewritten = w.Rows.Select(r => r.BroadcastTs is null ? r with { FileName = "y.xml", UnavailableReason = "unparsable", FileBytes = 9 } : r).ToList();
            var removed = w.Rows.Where(r => r.BroadcastTs is not null).ToList();

            Answer(added, w, counts: false).ShouldBe(baseline);
            Answer(rewritten, w, counts: false).ShouldBe(baseline);
            Answer(removed, w, counts: false).ShouldBe(baseline);
            PointInTimeRule.AsOf(added, w.T, w.Lag).UnavailableCount.ShouldBe(PointInTimeRule.AsOf(w.Rows, w.T, w.Lag).UnavailableCount + 2);
        }
    }

    [Fact]
    public void Control_changing_a_visible_current_filing_does_change_the_answer()
    {
        var random = new Random(20261104);
        var exercised = 0;
        for (var i = 0; i < Cases * 2; i++)
        {
            var w = Generate(random);
            var answer = PointInTimeRule.AsOf(w.Rows, w.T, w.Lag);
            if (answer.Filings.Count == 0)
            {
                continue;
            }

            exercised++;
            var target = answer.Filings[random.Next(answer.Filings.Count)];
            var changed = w.Rows.Select(r => r.Id == target.Id ? r with { FileBytes = r.FileBytes + 1 } : r).ToList();
            var removed = w.Rows.Where(r => r.Id != target.Id).ToList();

            Answer(changed, w).ShouldNotBe(Answer(w.Rows, w));
            Answer(removed, w).ShouldNotBe(Answer(w.Rows, w));
        }

        exercised.ShouldBeGreaterThanOrEqualTo(Cases);
    }

    [Fact]
    public void A_restatement_after_t_is_invisible_and_one_before_t_hides_its_predecessor()
    {
        var random = new Random(20261105);
        var after = 0;
        var before = 0;
        for (var i = 0; i < Cases * 2; i++)
        {
            var w = Generate(random);
            var current = PointInTimeRule.AsOf(w.Rows, w.T, w.Lag).Filings.Where(f => f.PeriodEnd is not null).ToList();
            if (current.Count == 0)
            {
                continue;
            }

            var target = current[random.Next(current.Count)];
            var groupLatest = w.Rows.Where(r => r.PeriodEnd == target.PeriodEnd && r.BroadcastTs is not null).Max(r => r.BroadcastTs!.Value);

            var late = FilingSamples.Ok(3000, target.PeriodEnd, new[] { w.Cutoff, groupLatest }.Max().AddMinutes(1 + random.Next(0, 4000)), target.Id);
            Answer([.. w.Rows, late], w).ShouldBe(Answer(w.Rows, w));
            after++;

            // a restatement exactly at the edge of visibility (inclusive), later than everything in its group
            if (w.Cutoff > groupLatest && target.BroadcastTs == groupLatest)
            {
                var edge = FilingSamples.Ok(3001, target.PeriodEnd, w.Cutoff, target.Id);
                var result = PointInTimeRule.AsOf([.. w.Rows, edge], w.T, w.Lag);

                result.Filings.Select(f => f.Id).ShouldContain(edge.Id);
                result.Filings.Select(f => f.Id).ShouldNotContain(target.Id);
                before++;
            }
        }

        after.ShouldBeGreaterThanOrEqualTo(Cases);
        before.ShouldBeGreaterThanOrEqualTo(Cases / 5);
    }

    [Fact]
    public void A_later_instant_never_un_sees_a_filing_that_no_later_visible_filing_replaced()
    {
        var random = new Random(20261106);
        for (var i = 0; i < Cases; i++)
        {
            var w = Generate(random);
            var later = new World { Rows = w.Rows, Lag = w.Lag, T = w.T.AddMinutes(random.Next(0, 9000)) };
            var first = PointInTimeRule.AsOf(w.Rows, w.T, w.Lag).Filings;
            var second = PointInTimeRule.AsOf(w.Rows, later.T, later.Lag).Filings;

            foreach (var row in first)
            {
                var kept = second.Any(s => s.Id == row.Id);
                var replaced = row.PeriodEnd is not null && second.Any(s => s.PeriodEnd == row.PeriodEnd && s.BroadcastTs > row.BroadcastTs);
                (kept || replaced).ShouldBeTrue("a filing disappeared without a later visible filing of its period");
            }
        }
    }

    [Fact]
    public void The_order_of_the_rows_and_the_ingest_times_do_not_change_the_answer()
    {
        var random = new Random(20261107);
        for (var i = 0; i < Cases; i++)
        {
            var w = Generate(random);
            var baseline = Answer(w.Rows, w);
            var shuffled = w.Rows.OrderBy(_ => random.Next()).ToList();
            var reingested = w.Rows.Select(r => r with { IngestedAt = Origin.AddMinutes(random.Next(0, 1_000_000)) }).OrderBy(_ => random.Next()).ToList();

            Answer(shuffled, w).ShouldBe(baseline);
            PointInTimeRule.AsOf(reingested, w.T, w.Lag).Filings.Select(f => f.Id).ShouldBe(PointInTimeRule.AsOf(w.Rows, w.T, w.Lag).Filings.Select(f => f.Id));
        }
    }
}
