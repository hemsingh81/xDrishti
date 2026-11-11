using System.Diagnostics;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>BK-11 (ADR 0261): the universe key part, the tier terciles, the parent-beats rule, the tier cells and the one-dimension guard, against hand numbers.</summary>
public sealed class UniverseCellTests
{
    private const string Six = "ORB|15m|BUY|intraday|FIXED_RR|*";

    private static readonly DateOnly AsOf = new(2026, 1, 31);

    private static readonly CellStatsOptions Options = new(RecencyHalfLifeDays: 30, PriorStrength: 20, MinTrades: 30);

    private static readonly CellId Orb = new("ORB", "15m", "BUY", "intraday", "FIXED_RR");

    private static readonly UniverseTierOptions Tier = new();

    private static CellTrade Trade(bool win, int daysBefore = 0) => new(AsOf.AddDays(-daysBefore), win ? 1 : -1, 0, 1);

    private static List<CellTrade> Trades(int wins, int losses) => [.. Enumerable.Repeat(Trade(true), wins), .. Enumerable.Repeat(Trade(false), losses)];

    private static Guid Id(int n) => new(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);

    // 1. key compatibility

    [Fact]
    public void A_six_part_key_parses_with_universe_star_and_keeps_its_text()
    {
        var id = CellId.TryParse(Six).ShouldNotBeNull();

        id.Universe.ShouldBe("*");
        id.Key.ShouldBe(Six);
    }

    [Theory]
    [InlineData("liq_high")]
    [InlineData("liq_mid")]
    [InlineData("liq_low")]
    public void A_seven_part_key_parses_with_its_tier_and_round_trips(string tier)
    {
        var key = Six + "|" + tier;

        var id = CellId.TryParse(key).ShouldNotBeNull();

        id.Universe.ShouldBe(tier);
        id.Key.ShouldBe(key);
        id.IsCash.ShouldBeTrue();
    }

    [Theory]
    [InlineData(Six + "|liq_x")]
    [InlineData(Six + "|a|b")]
    [InlineData("ORB|15m|BUY|intraday|FIXED_RR|*|liq_high|option_buy|extra")]
    public void An_unknown_universe_or_a_ninth_part_is_refused(string key)
    {
        CellId.TryParse(key).ShouldBeNull();
    }

    [Fact]
    public void A_tier_and_an_expression_make_the_eight_part_key()
    {
        var key = Six + "|liq_low|option_buy";

        CellId.TryParse(key)!.Key.ShouldBe(key);
    }

    // 2. parent link

    [Fact]
    public void The_chain_drops_the_regime_first_then_the_universe_and_ends_at_the_all_shares_cell()
    {
        var narrow = new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR", "UP_LOW_VOL", "liq_high");

        var first = narrow.Parent().ShouldNotBeNull();
        var second = first.Parent().ShouldNotBeNull();

        first.Key.ShouldBe(Six + "|liq_high");
        second.ShouldBe(Orb);
        second.Parent().ShouldBeNull();
        Orb.Parent().ShouldBeNull();
        new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR", "UP_LOW_VOL").Parent().ShouldBe(Orb);
    }

    // 3. tier by hand

    private static List<TierCandidate> Shares(int n, int bars = 100) => [.. Enumerable.Range(1, n).Select(i => new TierCandidate(Id(i), bars, 1000m - i))];

    [Fact]
    public void Three_hundred_shares_are_split_one_to_one_hundred_then_to_two_hundred_then_the_rest()
    {
        var table = UniverseTiers.Table(Shares(300), Tier);

        table.Count(t => t.Value == UniverseTier.High).ShouldBe(100);
        table.Count(t => t.Value == UniverseTier.Mid).ShouldBe(100);
        table.Count(t => t.Value == UniverseTier.Low).ShouldBe(100);
        table[Id(1)].ShouldBe(UniverseTier.High);
        table[Id(100)].ShouldBe(UniverseTier.High);
        table[Id(101)].ShouldBe(UniverseTier.Mid);
        table[Id(200)].ShouldBe(UniverseTier.Mid);
        table[Id(201)].ShouldBe(UniverseTier.Low);
    }

    [Theory]
    [InlineData(30, 10, 10, 10)]
    [InlineData(31, 11, 10, 10)]
    [InlineData(32, 11, 11, 10)]
    public void The_boundaries_are_ceil_n_over_three_and_ceil_two_n_over_three(int n, int high, int mid, int low)
    {
        var table = UniverseTiers.Table(Shares(n), Tier);

        (table.Count(t => t.Value == UniverseTier.High), table.Count(t => t.Value == UniverseTier.Mid), table.Count(t => t.Value == UniverseTier.Low)).ShouldBe((high, mid, low));
    }

    [Fact]
    public void Fewer_than_thirty_eligible_shares_have_no_tier_and_a_share_with_fewer_than_sixty_bars_is_unknown()
    {
        var shares = Shares(29);
        shares.Add(new TierCandidate(Id(99), 59, 5000m));

        var table = UniverseTiers.Table(shares, Tier);

        table[Id(99)].ShouldBe(UniverseTier.Unknown);
        table.Where(t => t.Key != Id(99)).ShouldAllBe(t => t.Value == UniverseTier.Unavailable);
        UniverseTiers.Assign(shares, Id(1234), Tier).ShouldBe(UniverseTier.Unknown);
    }

    [Fact]
    public void A_share_can_move_tier_between_two_dates_and_each_date_keeps_its_own()
    {
        var day1 = Shares(30);
        var day2 = Shares(30);
        day2[0] = new TierCandidate(Id(1), 100, 1m);

        UniverseTiers.Assign(day1, Id(1), Tier).ShouldBe(UniverseTier.High);
        UniverseTiers.Assign(day2, Id(1), Tier).ShouldBe(UniverseTier.Low);
    }

    // 4. no look-ahead

    private static Dictionary<Guid, IReadOnlyList<SessionValue>> Series(int shares, int sessions, DateOnly start, int seed)
    {
        var random = new Random(seed);
        var map = new Dictionary<Guid, IReadOnlyList<SessionValue>>();
        for (var i = 1; i <= shares; i++)
        {
            map[Id(i)] = [.. Enumerable.Range(0, sessions).Select(d => new SessionValue(start.AddDays(d), random.Next(1, 10_000)))];
        }

        return map;
    }

    [Fact]
    public void Appending_later_sessions_changes_no_earlier_assignment()
    {
        var start = new DateOnly(2025, 1, 1);
        var full = Series(40, 120, start, 3);
        var truncated = full.ToDictionary(p => p.Key, p => (IReadOnlyList<SessionValue>)[.. p.Value.Take(90)]);

        foreach (var offset in new[] { 70, 80, 90 })
        {
            var date = start.AddDays(offset);
            var a = UniverseTiers.Table(TierCandidates.At(full, date), Tier);
            var b = UniverseTiers.Table(TierCandidates.At(truncated, date), Tier);

            a.OrderBy(p => p.Key).ShouldBe(b.OrderBy(p => p.Key));
        }
    }

    [Fact]
    public void The_session_of_the_decision_date_itself_is_not_read()
    {
        var series = new Dictionary<Guid, IReadOnlyList<SessionValue>> { [Id(1)] = [.. Enumerable.Range(0, 61).Select(d => new SessionValue(new DateOnly(2025, 1, 1).AddDays(d), 5))] };

        TierCandidates.At(series, new DateOnly(2025, 1, 1).AddDays(60)).Single().Bars.ShouldBe(60);
        TierCandidates.At(series, new DateOnly(2025, 1, 1)).ShouldBeEmpty();   // no dated membership before its first session: unknown, never today's tier
    }

    // 5 and 6. shrinkage and the parent-beats rule

    private static (CellStatsRow Parent, CellStatsRow Child) Rows(int childWins, int childLosses, int parentWins = 100, int parentTotal = 200)
    {
        var child = Trades(childWins, childLosses);
        var rest = Trades(parentWins - childWins, parentTotal - parentWins - childLosses);
        var star = new CellInput(Orb, false, [.. child, .. rest]);
        var tiered = new CellInput(Orb with { Universe = "liq_high" }, false, child);
        var rows = CellStatsCalculator.Compute([star], AsOf, Options, universeCells: [tiered]);
        var all = rows.Where(r => r.Window == CellWindow.All).ToList();
        return (all.Single(r => r.Cell.Universe == "*"), all.Single(r => r.Cell.Universe == "liq_high"));
    }

    [Fact]
    public void A_child_with_22_wins_in_30_is_shrunk_toward_its_parent_and_read_when_its_bound_beats_the_parent_without_it()
    {
        var (parent, child) = Rows(22, 8);

        parent.WinRatePosterior.ShouldBe(0.5, 1e-9);
        child.PriorRate.ShouldBe(0.5, 1e-9);
        child.WinRatePosterior.ShouldBe(0.64, 1e-9);                       // (22 + 20 x 0.5) / (30 + 20)
        child.WinRateLower.ShouldBe(0.53, 0.01);
        var choice = CellChoice.Resolve(child, parent, 30, Options);

        choice.Reason.ShouldBe(CellChoice.ChildBetter);
        choice.Cell.ShouldBeSameAs(child);
        choice.IsChild.ShouldBeTrue();
        choice.ParentLower.ShouldNotBeNull().ShouldBeLessThan(choice.ChildLower);
    }

    [Fact]
    public void A_child_with_16_wins_in_30_is_no_better_than_all_shares_and_the_parent_is_read()
    {
        var (parent, child) = Rows(16, 14);

        child.WinRatePosterior.ShouldBe(0.52, 1e-9);
        child.WinRateLower.ShouldBe(0.40, 0.01);
        var choice = CellChoice.Resolve(child, parent, 30, Options);

        choice.Reason.ShouldBe(CellChoice.NoBetterThanParent);
        choice.Cell.ShouldBeSameAs(parent);
        choice.ParentLower.ShouldNotBeNull().ShouldBeGreaterThan(choice.ChildLower);
    }

    [Fact]
    public void A_child_with_29_trades_is_thin_and_the_parent_is_read()
    {
        var (parent, child) = Rows(25, 4);

        var choice = CellChoice.Resolve(child, parent, 30, Options);

        choice.Reason.ShouldBe(CellChoice.ChildThin);
        choice.Cell.ShouldBeSameAs(parent);
        choice.ParentLower.ShouldBeNull();
    }

    [Fact]
    public void Resolve_never_returns_a_child_whose_bound_is_at_or_below_the_parents()
    {
        var random = new Random(11);
        for (var i = 0; i < 200; i++)
        {
            var childN = random.Next(30, 80);
            var childWins = random.Next(0, childN + 1);
            var parentN = childN + random.Next(0, 300);
            var parentWins = childWins + random.Next(0, parentN - childN + 1);
            var (parent, child) = Rows(childWins, childN - childWins, parentWins, parentN);

            var choice = CellChoice.Resolve(child, parent, 30, Options);

            if (choice.IsChild)
            {
                choice.ChildLower.ShouldBeGreaterThan(choice.ParentLower!.Value);
            }
            else
            {
                choice.Cell.ShouldBeSameAs(parent);
            }
        }
    }

    [Fact]
    public void The_all_shares_rows_are_the_same_with_and_without_the_tier_cells_and_a_tier_cell_has_no_shrinkage_pass()
    {
        var star = new CellInput(Orb, false, Trades(60, 40));
        var tiered = new CellInput(Orb with { Universe = "liq_low" }, false, Trades(10, 10));

        var without = CellStatsCalculator.Compute([star], AsOf, Options);
        var with = CellStatsCalculator.Compute([star], AsOf, Options, universeCells: [tiered]);

        with.Take(without.Count).ShouldBe(without);
        with.Count.ShouldBe(without.Count * 2);
        Should.Throw<ArgumentException>(() => CellStatsCalculator.Compute([star], AsOf, Options, universeCells: [new CellInput(new CellId("OTHER", "15m", "BUY", "intraday", "FIXED_RR", Universe: "liq_low"), false, Trades(1, 1))]));
    }

    // tier cells

    [Fact]
    public void Tier_cells_and_the_untiered_trades_make_exactly_the_trades_of_the_cell()
    {
        var cell = new CellInput(Orb, false, Trades(3, 3));
        IReadOnlyList<UniverseTier> tiers = [UniverseTier.High, UniverseTier.High, UniverseTier.Low, UniverseTier.Unknown, UniverseTier.Unavailable, UniverseTier.Mid];

        var built = UniverseCellBuilder.Build([cell], [tiers]);

        built.Cells.Select(c => c.Cell.Key).ShouldBe([Six + "|liq_high", Six + "|liq_mid", Six + "|liq_low"]);
        (built.Cells.Sum(c => c.Trades.Count) + built.Unknown + built.Unavailable).ShouldBe(cell.Trades.Count);
        (built.Unknown, built.Unavailable).ShouldBe((1, 1));
        Should.Throw<ArgumentException>(() => UniverseCellBuilder.Build([cell], [[UniverseTier.High]]));
    }

    // 7. explosion guard

    [Fact]
    public void The_cells_are_at_most_four_times_the_plain_cells_and_the_key_has_exactly_one_extra_part()
    {
        CellExplosionGuard.Holds().ShouldBeTrue();
        var plain = Enumerable.Range(0, 10).Select(i => new CellId("S" + i, "15m", "BUY", "intraday", "FIXED_RR")).ToList();
        var all = plain.Concat(plain.SelectMany(p => CellId.Tiers.Select(t => p with { Universe = t }))).ToList();

        CellExplosionGuard.WithinBound(all).ShouldBeTrue();
        CellExplosionGuard.WithinBound([.. all, plain[0] with { Universe = "liq_high", Regime = "X" }, .. all]).ShouldBeFalse();
        all.Count.ShouldBe(4 * plain.Count);
    }

    // 14. performance (explicit: wall clock)

    [Fact(Explicit = true)]
    public void Tagging_twenty_thousand_trades_takes_under_two_seconds()
    {
        var start = new DateOnly(2023, 1, 1);
        var series = Series(400, 750, start, 5);
        var dates = Enumerable.Range(100, 650).Select(d => start.AddDays(d)).ToList();
        var watch = Stopwatch.StartNew();
        var tables = dates.ToDictionary(d => d, d => UniverseTiers.Table(TierCandidates.At(series, d), Tier));
        var random = new Random(1);
        var tagged = 0;
        for (var i = 0; i < 20_000; i++)
        {
            tagged += tables[dates[random.Next(dates.Count)]].GetValueOrDefault(Id(random.Next(1, 401))) == UniverseTier.High ? 1 : 0;
        }

        watch.Elapsed.TotalSeconds.ShouldBeLessThan(2);
        tagged.ShouldBeGreaterThan(0);
    }
}
