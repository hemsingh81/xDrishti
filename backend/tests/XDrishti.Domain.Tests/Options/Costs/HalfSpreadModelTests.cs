using XDrishti.Domain.Options.Costs;

namespace XDrishti.Domain.Tests.Options.Costs;

/// <summary>
/// P14-07 criteria 3 and 4: the half-spread model on synthetic sessions with known quotes. Prices are paise; a quote around a mid of 10,000 paise with a half-gap of d paise has a half-spread of d / 100 percent, so the hand values are exact.
/// </summary>
public sealed class HalfSpreadModelTests
{
    private static readonly DateOnly Day = new(2026, 5, 4);
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    private const int Atm = 2_200_000;
    private const int Step = 5_000;

    /// <summary>One snapshot: the ATM strike quoted with the half-gap and the next strike one-sided (so the step is known and the next strike counts nothing).</summary>
    private static SpreadQuote[] Snapshot(int hour, int minute, int second, int halfGap, int expiryDays = 1, int strike = Atm)
    {
        var ts = new DateTimeOffset(Day.Year, Day.Month, Day.Day, hour, minute, second, India);
        var expiry = Day.AddDays(expiryDays);
        return
        [
            new SpreadQuote(ts, expiry, strike, Atm + 500, 10_000 - halfGap, 10_000 + halfGap),
            new SpreadQuote(ts, expiry, strike + Step, Atm + 500, null, 10_100),
        ];
    }

    /// <summary>One snapshot whose only quote is <paramref name="steps"/> strike steps above the ATM strike (the ATM strike itself is one-sided).</summary>
    private static SpreadQuote[] Far(int hour, int minute, int second, int halfGap, int steps)
    {
        var ts = new DateTimeOffset(Day.Year, Day.Month, Day.Day, hour, minute, second, India);
        var expiry = Day.AddDays(1);
        return
        [
            new SpreadQuote(ts, expiry, Atm, Atm + 500, null, 10_100),
            new SpreadQuote(ts, expiry, Atm + steps * Step, Atm + 500, 10_000 - halfGap, 10_000 + halfGap),
        ];
    }

    [Fact]
    public void Each_bucket_median_is_the_hand_value()
    {
        var accumulator = new HalfSpreadAccumulator();
        List<SpreadQuote> quotes = [];
        foreach (var half in new[] { 50, 100, 150 })
        {
            quotes.AddRange(Snapshot(10, 30, half % 60, half));
        }

        quotes.AddRange(Snapshot(9, 30, 0, 200));
        quotes.AddRange(Snapshot(9, 31, 0, 400));
        quotes.AddRange(Snapshot(15, 0, 0, 300));
        quotes.AddRange(Snapshot(10, 40, 0, 100, expiryDays: 40));
        accumulator.AddSession(Day, quotes);

        var rows = accumulator.Rows(1).ToDictionary(r => (r.Moneyness, r.Dte, r.Tod));

        rows[("0", "0-2", "10:00-14:30")].ShouldBe(new SpreadBucketRow("0", "0-2", "10:00-14:30", 3, 1.0));
        rows[("0", "0-2", "09:20-10:00")].ShouldBe(new SpreadBucketRow("0", "0-2", "09:20-10:00", 2, 3.0)); // 2.0 and 4.0 -> 3.0
        rows[("0", "0-2", "14:30-15:30")].ShouldBe(new SpreadBucketRow("0", "0-2", "14:30-15:30", 1, 3.0));
        rows[("0", "31+", "10:00-14:30")].ShouldBe(new SpreadBucketRow("0", "31+", "10:00-14:30", 1, 1.0));
        rows[("0", "0-2", SpreadBuckets.All)].N.ShouldBe(6);
        rows[("0", "0-2", SpreadBuckets.All)].HalfSpreadPct.ShouldBe(1.75); // sorted 0.5 1.0 1.5 2.0 3.0 4.0: the middle two are 1.5 and 2.0
    }

    [Fact]
    public void The_median_of_an_even_count_is_the_mean_of_the_two_middle_values()
    {
        HalfSpreadAccumulator.Median([0.5, 1.0]).ShouldBe(0.75);
        HalfSpreadAccumulator.Median([3.0, 1.0, 2.0]).ShouldBe(2.0);
        HalfSpreadAccumulator.Median([1.0, 1.0, 1.0, 9.0]).ShouldBe(1.0);
    }

    [Fact]
    public void One_sided_crossed_out_of_hours_and_far_out_quotes_are_never_used_and_are_counted()
    {
        var accumulator = new HalfSpreadAccumulator();
        var ts = new DateTimeOffset(Day.Year, Day.Month, Day.Day, 11, 0, 0, India);
        var expiry = Day.AddDays(1);
        var late = ts.AddHours(5); // 16:00
        accumulator.AddSession(Day,
        [
            new SpreadQuote(ts, expiry, Atm, Atm, 9_900, 10_100),
            new SpreadQuote(ts, expiry, Atm + Step, Atm, 10_200, 10_100), // crossed
            new SpreadQuote(ts, expiry, Atm + 2 * Step, Atm, null, 10_100), // one-sided
            new SpreadQuote(ts, expiry, Atm + 3 * Step, Atm, 0, 10_100), // a zero bid
            new SpreadQuote(ts, expiry, Atm + 12 * Step, Atm, 9_900, 10_100), // 12 steps out
            new SpreadQuote(late, expiry, Atm, Atm, 9_900, 10_100), // after 15:30
            new SpreadQuote(late, expiry, Atm + Step, Atm, 9_900, 10_100),
            new SpreadQuote(ts.AddMinutes(1), expiry, Atm, Atm, 9_900, 10_100), // a snapshot with one strike: no step
            new SpreadQuote(ts, Day.AddDays(-1), Atm, Atm, 9_900, 10_100), // an expired expiry
            new SpreadQuote(ts, Day.AddDays(-1), Atm + Step, Atm, 9_900, 10_100),
        ]);

        accumulator.Used.ShouldBe(1);
        accumulator.Skipped.ShouldBe(9);
        accumulator.Rows(1).Where(r => r.Tod != SpreadBuckets.All && r.Moneyness != SpreadBuckets.All).ShouldHaveSingleItem().ShouldBe(new SpreadBucketRow("0", "0-2", "10:00-14:30", 1, 1.0));
    }

    [Fact]
    public void A_locked_quote_with_the_bid_equal_to_the_ask_is_skipped_never_counted_as_a_zero_spread()
    {
        var accumulator = new HalfSpreadAccumulator();
        var ts = new DateTimeOffset(Day.Year, Day.Month, Day.Day, 11, 0, 0, India);
        var expiry = Day.AddDays(1);

        accumulator.AddSession(Day, [new SpreadQuote(ts, expiry, Atm, Atm, 10_000, 10_000), new SpreadQuote(ts, expiry, Atm + Step, Atm, 9_900, 10_100)]);

        new SpreadQuote(ts, expiry, Atm, Atm, 10_000, 10_000).TwoSided.ShouldBeFalse();
        accumulator.Used.ShouldBe(1);
        accumulator.Skipped.ShouldBe(1);
        accumulator.Rows(1).ShouldNotContain(r => r.HalfSpreadPct == 0.0);
    }

    [Fact]
    public void The_moneyness_is_counted_in_strike_steps_from_the_nearest_strike_to_the_underlying_price()
    {
        var accumulator = new HalfSpreadAccumulator();
        var ts = new DateTimeOffset(Day.Year, Day.Month, Day.Day, 11, 0, 0, India);
        var expiry = Day.AddDays(5);
        // the underlying is 4,200 paise above Atm: the nearest strike is Atm + Step (800 paise away), so Atm is one step out and Atm + 3 steps is two
        accumulator.AddSession(Day, [.. Enumerable.Range(0, 5).Select(i => new SpreadQuote(ts, expiry, Atm + i * Step, Atm + 4_200, 9_900, 10_100))]);

        var rows = accumulator.Rows(1).Where(r => r.Tod == "10:00-14:30").ToDictionary(r => r.Moneyness);

        rows["0"].N.ShouldBe(1); // Atm + Step
        rows["1-2"].N.ShouldBe(3); // Atm (1 step), Atm + 2 steps (1), Atm + 3 steps (2)
        rows["3-5"].N.ShouldBe(1); // Atm + 4 steps (3)
    }

    [Fact]
    public void A_bucket_of_150_quotes_merges_its_time_of_day_and_a_still_short_bucket_merges_its_moneyness_too()
    {
        var accumulator = new HalfSpreadAccumulator();
        var quotes = new List<SpreadQuote>();
        for (var i = 0; i < 150; i++)
        {
            quotes.AddRange(Snapshot(10, 30 + i / 60, i % 60, 100)); // 150 quotes of 1.0 % in (0, 0-2, 10:00-14:30)
        }

        for (var i = 0; i < 100; i++)
        {
            quotes.AddRange(Snapshot(9, 30 + i / 60, i % 60, 300)); // 100 quotes of 3.0 % in (0, 0-2, 09:20-10:00)
        }

        for (var i = 0; i < 120; i++)
        {
            quotes.AddRange(Far(12, i / 60, i % 60, 500, 7)); // 120 quotes at 7 steps: (6-10, 0-2, 10:00-14:30), 5.0 %
        }

        accumulator.AddSession(Day, quotes);
        var model = new HalfSpreadModel("m", Guid.Empty, 60, Day, Day, accumulator.Rows(200));

        model.Lookup(new SpreadBucket("0", "0-2", "10:00-14:30")).ShouldNotBeNull().ShouldBe(new SpreadBucketRow("0", "0-2", SpreadBuckets.All, 250, 1.0));
        model.Lookup(new SpreadBucket("0", "0-2", "09:20-10:00")).ShouldNotBeNull().Tod.ShouldBe(SpreadBuckets.All);
        // 120 at 5.0 % is short of 200 and so is its (moneyness, dte) pool; the days-to-expiry pool is 150 + 100 + 120 = 370 quotes, median of the sorted 1.0 x150, 3.0 x100, 5.0 x120 is 3.0
        model.Lookup(new SpreadBucket("6-10", "0-2", "10:00-14:30")).ShouldNotBeNull().ShouldBe(new SpreadBucketRow(SpreadBuckets.All, "0-2", SpreadBuckets.All, 370, 3.0));
        // nothing in other days-to-expiry buckets: unavailable, never a number
        model.Lookup(new SpreadBucket("0", "8-30", "10:00-14:30")).ShouldBeNull();
    }

    [Fact]
    public void A_bucket_with_enough_quotes_keeps_its_own_median_even_when_the_pool_is_larger()
    {
        var accumulator = new HalfSpreadAccumulator();
        var quotes = new List<SpreadQuote>();
        for (var i = 0; i < 200; i++)
        {
            quotes.AddRange(Snapshot(11, i / 60, i % 60, 50));
        }

        for (var i = 0; i < 10; i++)
        {
            quotes.AddRange(Snapshot(15, 0, i, 900));
        }

        accumulator.AddSession(Day, quotes);
        var model = new HalfSpreadModel("m", Guid.Empty, 60, Day, Day, accumulator.Rows(200));

        model.Lookup(new SpreadBucket("0", "0-2", "10:00-14:30")).ShouldBe(new SpreadBucketRow("0", "0-2", "10:00-14:30", 200, 0.5));
    }

    [Fact]
    public void The_resolver_prefers_the_measured_bucket_then_the_owners_assumption_then_unavailable_and_labels_each()
    {
        var model = new HalfSpreadModel("m", Guid.Empty, 60, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), [new SpreadBucketRow("0", "0-2", "10:00-14:30", 250, 0.8)]);
        var bucket = new SpreadBucket("0", "0-2", "10:00-14:30");
        var other = new SpreadBucket("3-5", "0-2", "10:00-14:30");

        var measured = HalfSpreadResolver.Resolve(model, 0.5m, bucket, new DateOnly(2026, 3, 1));
        var shortBucket = HalfSpreadResolver.Resolve(model, 0.5m, other, new DateOnly(2026, 3, 1));
        var assumedOnly = HalfSpreadResolver.Resolve(null, 0.5m, bucket, new DateOnly(2026, 3, 1));
        var neither = HalfSpreadResolver.Resolve(null, null, bucket, new DateOnly(2026, 3, 1));
        var shortNoAssumption = HalfSpreadResolver.Resolve(model, null, other, new DateOnly(2026, 3, 1));
        var noBucket = HalfSpreadResolver.Resolve(model, null, null, new DateOnly(2026, 3, 1));

        measured.Source.ShouldBe(HalfSpreadSource.Measured);
        measured.Pct.ShouldBe(0.8m);
        measured.Label.ShouldBe("spread: measured (60 sessions, from 2026-01-01 to 2026-04-01)");
        measured.FromRecentArchive.ShouldBeFalse();
        shortBucket.Label.ShouldBe("spread: assumed 0.5 % by owner");
        assumedOnly.Pct.ShouldBe(0.5m);
        assumedOnly.Source.ShouldBe(HalfSpreadSource.Assumed);
        neither.Available.ShouldBeFalse();
        neither.Pct.ShouldBeNull();
        neither.Reason.ShouldBe("spread_unavailable");
        shortNoAssumption.Available.ShouldBeFalse();
        noBucket.Available.ShouldBeFalse();
    }

    [Fact]
    public void A_measured_model_used_before_its_first_session_is_flagged_as_a_recent_archive_spread()
    {
        var model = new HalfSpreadModel("m", Guid.Empty, 60, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), [new SpreadBucketRow("0", "0-2", "10:00-14:30", 250, 0.8)]);
        var bucket = new SpreadBucket("0", "0-2", "10:00-14:30");

        var early = HalfSpreadResolver.Resolve(model, null, bucket, new DateOnly(2025, 12, 31));
        var onTheDay = HalfSpreadResolver.Resolve(model, null, bucket, new DateOnly(2026, 1, 1));

        early.FromRecentArchive.ShouldBeTrue();
        early.Label.ShouldBe("spread: measured (60 sessions, from 2026-01-01 to 2026-04-01); spread_from_recent_archive");
        onTheDay.FromRecentArchive.ShouldBeFalse();
        onTheDay.Label.ShouldBe("spread: measured (60 sessions, from 2026-01-01 to 2026-04-01)");
        HalfSpreadValue.SpreadFromRecentArchive.ShouldBe("spread_from_recent_archive");
    }

    [Fact]
    public void An_uninitialised_value_is_not_available_and_the_bucket_words_cover_their_ranges()
    {
        new HalfSpreadValue(default, null, string.Empty, null, false).Available.ShouldBeFalse();
        SpreadBuckets.Of(0, 0, new TimeOnly(9, 20)).ShouldBe(new SpreadBucket("0", "0-2", "09:20-10:00"));
        SpreadBuckets.Of(2, 7, new TimeOnly(10, 0)).ShouldBe(new SpreadBucket("1-2", "3-7", "10:00-14:30"));
        SpreadBuckets.Of(5, 30, new TimeOnly(14, 30)).ShouldBe(new SpreadBucket("3-5", "8-30", "14:30-15:30"));
        SpreadBuckets.Of(10, 31, new TimeOnly(15, 30)).ShouldBe(new SpreadBucket("6-10", "31+", "14:30-15:30"));
        SpreadBuckets.Of(11, 1, new TimeOnly(11, 0)).ShouldBeNull();
        SpreadBuckets.Of(1, -1, new TimeOnly(11, 0)).ShouldBeNull();
        SpreadBuckets.Of(1, 1, new TimeOnly(9, 19)).ShouldBeNull();
        SpreadBuckets.Of(1, 1, new TimeOnly(15, 31)).ShouldBeNull();
    }

    [Fact]
    public void The_model_id_is_the_hash_of_the_inputs_and_changes_with_each_of_them()
    {
        var u = Guid.Parse("00000000-0000-7000-8000-000000000013");
        DateOnly[] sessions = [new(2026, 1, 2), new(2026, 1, 1)];

        var id = SpreadModelId.Of(u, sessions, 60, 200);

        id.Length.ShouldBe(64);
        SpreadModelId.Of(u, [sessions[1], sessions[0]], 60, 200).ShouldBe(id);
        SpreadModelId.Of(u, sessions, 61, 200).ShouldNotBe(id);
        SpreadModelId.Of(u, sessions, 60, 201).ShouldNotBe(id);
        SpreadModelId.Of(Guid.Empty, sessions, 60, 200).ShouldNotBe(id);
        SpreadModelId.Of(u, [sessions[0]], 60, 200).ShouldNotBe(id);
        SpreadModelId.ModelVersion.ShouldBe(1);
        SpreadModelId.SettingsJson(60, 200).ShouldBe("{\"model_version\":1,\"min_usable_sessions\":60,\"min_quotes_per_bucket\":200}");
    }
}
