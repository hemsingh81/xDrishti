using XDrishti.Domain.Market.Context;

namespace XDrishti.Domain.Tests.Market.Context;

/// <summary>P3-01e criteria 1 to 4: the breadth of the owner's tracked shares on a day, point in time, with the reason for every share that did not vote.</summary>
public sealed class BreadthCalculatorTests
{
    private static readonly BreadthOptions Hand = new(20, 5, 0.5);

    private static readonly IReadOnlyDictionary<Guid, IReadOnlySet<DateOnly>> NoActions = new Dictionary<Guid, IReadOnlySet<DateOnly>>();

    /// <summary>40 weekdays from 2026-01-05, so a window of 20 ends at the 20th and the sessions go on after it.</summary>
    private static readonly DateOnly[] Sessions = WeekdaysFrom(new DateOnly(2026, 1, 5), 40);

    private static readonly DateOnly D = Sessions[19];

    private static DateOnly[] WeekdaysFrom(DateOnly start, int count)
    {
        var list = new List<DateOnly>();
        for (var d = start; list.Count < count; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                list.Add(d);
            }
        }

        return [.. list];
    }

    private static BreadthInput Share(Guid id, IEnumerable<double> closes, int firstSession = 0) =>
        new(id, closes.Select((c, i) => (Date: Sessions[firstSession + i], Close: c)).ToDictionary(x => x.Date, x => x.Close));

    private static IEnumerable<double> Up => Enumerable.Range(1, 20).Select(i => (double)i);

    private static IEnumerable<double> Down => Enumerable.Range(1, 20).Select(i => (double)(21 - i));

    private static (List<BreadthInput> Shares, Dictionary<Guid, IReadOnlySet<DateOnly>> Actions) HandFixture()
    {
        var ids = Enumerable.Range(0, 9).Select(_ => Guid.NewGuid()).ToArray();
        var shares = new List<BreadthInput>
        {
            Share(ids[0], Up), Share(ids[1], Up), Share(ids[2], Up), // A, B, C: close 20 over a mean of 10.5
            Share(ids[3], Down), Share(ids[4], Down), // D, E: close 1
            Share(ids[5], Enumerable.Repeat(10.0, 20)), // F: equal to its mean
            Share(ids[6], Up.Skip(1), firstSession: 1), // G: 19 bars
            Share(ids[7], Up.Select(c => c).Take(19)), // H: no bar on D
            Share(ids[8], Up), // I: split in the window
        };
        return (shares, new Dictionary<Guid, IReadOnlySet<DateOnly>> { [ids[8]] = new HashSet<DateOnly> { Sessions[11] } });
    }

    [Fact]
    public void The_hand_fixture_gives_50_percent_of_6_voters_of_9_shares_with_the_three_reasons()
    {
        var (shares, actions) = HandFixture();

        var result = BreadthCalculator.Compute(D, Sessions, shares, actions, Hand);

        result.Value.ShouldBe(50.0);
        (result.Voters, result.Universe, result.Excluded, result.State).ShouldBe((6, 9, 3, BreadthResult.ValueState));
        result.ExcludedByReason.ShouldBe(new Dictionary<string, int> { ["action_in_window"] = 1, ["no_bar"] = 1, ["warmup"] = 1 });
    }

    [Fact]
    public void Equal_to_the_mean_is_not_above()
    {
        var shares = new List<BreadthInput> { Share(Guid.NewGuid(), Enumerable.Repeat(10.0, 20)) };

        var result = BreadthCalculator.Compute(D, Sessions, shares, NoActions, new BreadthOptions(20, 1, 0.5));

        result.Value.ShouldBe(0.0);
        result.Voters.ShouldBe(1);
    }

    [Fact]
    public void Exactly_the_minimum_voters_gives_a_number_and_one_fewer_does_not()
    {
        var shares = Enumerable.Range(0, 5).Select(_ => Share(Guid.NewGuid(), Up)).ToList();

        BreadthCalculator.Compute(D, Sessions, shares, NoActions, new BreadthOptions(20, 5, 0.5)).State.ShouldBe(BreadthResult.ValueState);
        var thin = BreadthCalculator.Compute(D, Sessions, shares, NoActions, new BreadthOptions(20, 6, 0.5));

        thin.State.ShouldBe(BreadthResult.Insufficient);
        thin.Value.ShouldBeNull();
        (thin.Voters, thin.Universe).ShouldBe((5, 5));
    }

    [Fact]
    public void Exactly_the_minimum_share_of_the_shares_with_a_bar_gives_a_number_and_a_hair_under_does_not()
    {
        // 4 shares have a bar on D; 2 of them vote (the other 2 lack history): exactly half.
        var shares = new List<BreadthInput> { Share(Guid.NewGuid(), Up), Share(Guid.NewGuid(), Up), Share(Guid.NewGuid(), Up.Skip(10), 10), Share(Guid.NewGuid(), Up.Skip(10), 10) };

        BreadthCalculator.Compute(D, Sessions, shares, NoActions, new BreadthOptions(20, 2, 0.5)).State.ShouldBe(BreadthResult.ValueState);
        BreadthCalculator.Compute(D, Sessions, shares, NoActions, new BreadthOptions(20, 2, 0.51)).State.ShouldBe(BreadthResult.Insufficient);
    }

    [Fact]
    public void No_voters_is_insufficient_even_with_a_minimum_of_one_and_nothing_is_divided_by_zero()
    {
        var result = BreadthCalculator.Compute(D, Sessions, [], NoActions, new BreadthOptions(20, 1, 0.1));

        (result.Value, result.State, result.Voters, result.Universe).ShouldBe((null, BreadthResult.Insufficient, 0, 0));
    }

    [Fact]
    public void The_window_is_the_sessions_of_the_calendar_not_calendar_days_so_a_holiday_gap_is_crossed()
    {
        // Sessions skip the weekend and a holiday on Wednesday 2026-01-14: the 20 sessions ending on D start earlier in calendar days.
        var sessions = Sessions.Where(d => d != new DateOnly(2026, 1, 14)).ToArray();
        var day = sessions[19];
        var closes = sessions.Take(20).Select((d, i) => (d, (double)(i + 1))).ToDictionary(x => x.d, x => x.Item2);

        var result = BreadthCalculator.Compute(day, sessions, [new BreadthInput(Guid.NewGuid(), closes)], NoActions, new BreadthOptions(20, 1, 0.5));

        result.Voters.ShouldBe(1);
        result.Value.ShouldBe(100.0);
    }

    [Fact]
    public void The_Muhurat_evening_on_a_sunday_is_a_session_like_any_other()
    {
        var sunday = new DateOnly(2026, 11, 8);
        var sessions = Sessions.Take(19).Append(sunday).ToArray();
        var closes = sessions.Select((d, i) => (d, (double)(i + 1))).ToDictionary(x => x.d, x => x.Item2);

        var result = BreadthCalculator.Compute(sunday, sessions, [new BreadthInput(Guid.NewGuid(), closes)], NoActions, new BreadthOptions(20, 1, 0.5));

        (result.Voters, result.Value).ShouldBe((1, 100.0));
    }

    [Fact]
    public void A_date_that_is_not_a_session_is_not_a_number_and_every_share_is_counted_as_having_no_bar()
    {
        var saturday = new DateOnly(2026, 1, 3);
        var result = BreadthCalculator.Compute(saturday, Sessions, [Share(Guid.NewGuid(), Up)], NoActions, Hand);

        (result.State, result.Voters, result.ExcludedByReason["no_bar"]).ShouldBe((BreadthResult.Insufficient, 0, 1));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(double.NaN)]
    public void A_close_that_is_not_a_positive_number_in_the_window_excludes_the_share_as_bad_bar(double bad)
    {
        var closes = Up.ToList();
        closes[7] = bad;

        var result = BreadthCalculator.Compute(D, Sessions, [Share(Guid.NewGuid(), closes), Share(Guid.NewGuid(), Up)], NoActions, new BreadthOptions(20, 1, 0.5));

        (result.Voters, result.ExcludedByReason["bad_bar"]).ShouldBe((1, 1));
    }

    [Fact]
    public void An_action_after_the_day_or_before_the_window_does_not_exclude_the_share()
    {
        var id = Guid.NewGuid();
        var actions = new Dictionary<Guid, IReadOnlySet<DateOnly>> { [id] = new HashSet<DateOnly> { Sessions[0].AddDays(-1), Sessions[25] } };

        var result = BreadthCalculator.Compute(D, Sessions, [Share(id, Up)], actions, new BreadthOptions(20, 1, 0.5));

        result.Voters.ShouldBe(1);
    }

    [Fact]
    public void An_action_on_the_first_and_on_the_last_session_of_the_window_both_exclude()
    {
        foreach (var exDate in new[] { Sessions[0], D })
        {
            var id = Guid.NewGuid();
            var result = BreadthCalculator.Compute(D, Sessions, [Share(id, Up)], new Dictionary<Guid, IReadOnlySet<DateOnly>> { [id] = new HashSet<DateOnly> { exDate } }, new BreadthOptions(20, 1, 0.5));

            result.ExcludedByReason.ShouldContainKeyAndValue("action_in_window", 1);
        }
    }

    [Fact]
    public void A_share_without_a_bar_on_the_day_is_counted_never_read_from_an_earlier_day()
    {
        var id = Guid.NewGuid();
        var withoutD = Share(id, Up.Take(19));

        var result = BreadthCalculator.Compute(D, Sessions, [withoutD], NoActions, new BreadthOptions(20, 1, 0.5));

        (result.Voters, result.Universe, result.ExcludedByReason["no_bar"]).ShouldBe((0, 1, 1));
    }

    // Criterion 3: nothing after D is read. The negative control reads D+1 and must be caught.
    public static TheoryData<int> Seeds() => [.. Enumerable.Range(1, 40)];

    private static List<BreadthInput> RandomShares(int seed, int count, int sessions)
    {
        var random = new Random(seed);
        var shares = new List<BreadthInput>();
        for (var s = 0; s < count; s++)
        {
            var closes = new Dictionary<DateOnly, double>();
            var price = 50 + random.NextDouble() * 500;
            for (var i = 0; i < sessions; i++)
            {
                price *= 1 + (random.NextDouble() - 0.5) * 0.06;
                if (random.Next(40) != 0)
                {
                    closes[Sessions[i]] = price;
                }
            }

            shares.Add(new BreadthInput(Guid.NewGuid(), closes));
        }

        return shares;
    }

    private static List<BreadthInput> Altered(List<BreadthInput> shares, int seed, bool remove)
    {
        var random = new Random(seed + 1000);
        return [.. shares.Select(s => new BreadthInput(
            s.ShareId,
            s.Closes.Where(kv => !(remove && kv.Key > D)).ToDictionary(kv => kv.Key, kv => kv.Key > D ? kv.Value * (0.2 + random.NextDouble() * 5) : kv.Value)))];
    }

    private static string Show(BreadthResult r) => $"{r.Value}|{r.Voters}|{r.Universe}|{r.Excluded}|{r.State}|{string.Join(',', r.ExcludedByReason.Select(kv => kv.Key + kv.Value))}";

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Changing_or_removing_every_close_after_the_day_changes_nothing(int seed)
    {
        var shares = RandomShares(seed, 60, 40);
        var options = new BreadthOptions(20, 5, 0.5);
        var expected = Show(BreadthCalculator.Compute(D, Sessions, shares, NoActions, options));

        Show(BreadthCalculator.Compute(D, Sessions, Altered(shares, seed, remove: false), NoActions, options)).ShouldBe(expected);
        Show(BreadthCalculator.Compute(D, Sessions, Altered(shares, seed, remove: true), NoActions, options)).ShouldBe(expected);
    }

    [Fact]
    public void The_negative_control_a_calculator_that_reads_the_next_session_is_caught_on_at_least_ten_seeds()
    {
        var caught = 0;
        var options = new BreadthOptions(20, 5, 0.5);
        for (var seed = 1; seed <= 40; seed++)
        {
            var shares = RandomShares(seed, 60, 40);
            var altered = Altered(shares, seed, remove: false);
            // A leaky definition: the window ends one session after D.
            var honest = Show(BreadthCalculator.Compute(D, Sessions, shares, NoActions, options));
            var leaky = Show(BreadthCalculator.Compute(Sessions[20], Sessions, shares, NoActions, options));
            var leakyAltered = Show(BreadthCalculator.Compute(Sessions[20], Sessions, altered, NoActions, options));
            if (leaky != leakyAltered && honest == Show(BreadthCalculator.Compute(D, Sessions, altered, NoActions, options)))
            {
                caught++;
            }
        }

        caught.ShouldBeGreaterThanOrEqualTo(10, "the truncation test can tell a calculator that reads D+1 from one that does not");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_order_of_the_shares_does_not_matter(int seed)
    {
        var shares = RandomShares(seed, 40, 40);
        var options = new BreadthOptions(20, 5, 0.5);

        var shuffled = shares.OrderBy(_ => new Random(seed).Next()).Reverse().ToList();

        Show(BreadthCalculator.Compute(D, Sessions, shuffled, NoActions, options)).ShouldBe(Show(BreadthCalculator.Compute(D, Sessions, shares, NoActions, options)));
    }

    [Fact]
    public void A_share_that_cannot_vote_changes_only_the_universe_and_the_exclusions()
    {
        var shares = RandomShares(7, 60, 40);
        var options = new BreadthOptions(20, 5, 0.1);
        var before = BreadthCalculator.Compute(D, Sessions, shares, NoActions, options);

        var extra = Share(Guid.NewGuid(), Up.Take(5), firstSession: 15); // only 5 bars: warmup
        var after = BreadthCalculator.Compute(D, Sessions, [.. shares, extra], NoActions, options);

        (after.Value, after.Voters).ShouldBe((before.Value, before.Voters));
        (after.Universe, after.Excluded).ShouldBe((before.Universe + 1, before.Excluded + 1));
    }
}
