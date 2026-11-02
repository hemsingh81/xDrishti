#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Learning;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Learning;

/// <summary>
/// P15-05 criteria 1 and 9 (the guard of the look-ahead rule through the extractor and the rules of the ports), 200 generated worlds per property, fixed seed: changing, adding or removing any input that is not visible at the decision instant (broadcast or intimation plus lag after t, undated
/// rows, bars after t, an action or an index close after the date) leaves every <c>ev-1</c> and <c>fu-1</c> value byte-identical; changing a visible input changes at least one value; the order of the inputs never matters.
/// </summary>
public sealed class ExtensionPointInTimePropertyTests
{
    private const int Cases = 200;

    private static readonly Guid Who = new("11111111-1111-1111-1111-111111111111");

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly TimeSpan Lag = TimeSpan.FromMinutes(5);

    private static readonly DateOnly FirstQuarter = new(2022, 3, 31);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int _lastFactReads;

    private sealed class World
    {
        public DateTimeOffset At { get; set; }

        public List<ResultFilingFacts> Results { get; } = [];

        public List<ShareholdingEnvelope> Holdings { get; } = [];

        public List<CalendarRow> Rows { get; } = [];

        public List<Bar> Bars { get; set; } = [];

        public List<AppliedAction> Actions { get; } = [];

        public Dictionary<DateOnly, double> Index { get; } = [];

        public World Copy()
        {
            var copy = new World { At = At, Bars = [.. Bars] };
            copy.Results.AddRange(Results);
            copy.Holdings.AddRange(Holdings);
            copy.Rows.AddRange(Rows);
            copy.Actions.AddRange(Actions);
            foreach (var (k, v) in Index)
            {
                copy.Index[k] = v;
            }

            return copy;
        }
    }

    private static string Sha(int n) => n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset Ist0(DateOnly date, int minute) => new(date.ToDateTime(TimeOnly.MinValue).AddMinutes(minute), Ist);

    private static bool Visible(DateTimeOffset? ts, DateTimeOffset at) => ts is { } time && time + Lag <= at;

    private static ResultFilingFacts Results(Random random, ref int n, DateOnly period, DateTimeOffset ts, bool undated = false)
    {
        n++;
        var sha = Sha(n);
        var filing = undated
            ? new StoredFiling(FilingId.Create(Who, FilingKind.Results, period, sha), Who, FilingKind.Results, period, null, BroadcastState.Unavailable, "date_only", ts, sha, 10, "r.xml", null)
            : new StoredFiling(FilingId.Create(Who, FilingKind.Results, period, sha), Who, FilingKind.Results, period, ts.ToUniversalTime(), BroadcastState.Ok, null, ts, sha, 10, "r.xml", null);
        var facts = new List<StoredResultFact>();
        foreach (var basis in new[] { ResultCodes.Consolidated, ResultCodes.Standalone })
        {
            if (basis == ResultCodes.Standalone && random.Next(3) != 0)
            {
                continue;
            }

            facts.Add(new StoredResultFact(ResultCodes.EpsBasic, ResultCodes.Quarter, basis, Math.Round((decimal)(random.NextDouble() * 20), 2), ResultCodes.InrPerShare, null));
            facts.Add(new StoredResultFact(ResultCodes.Revenue, ResultCodes.Quarter, basis, Math.Round((decimal)(100 + random.NextDouble() * 900), 2), ResultCodes.Inr, null));
            facts.Add(new StoredResultFact(ResultCodes.Pat, ResultCodes.Quarter, basis, Math.Round((decimal)(random.NextDouble() * 90), 2), ResultCodes.Inr, null));
        }

        return new ResultFilingFacts(filing, facts);
    }

    private static ShareholdingEnvelope Holding(Random random, ref int n, DateOnly period, DateTimeOffset ts, bool undated = false)
    {
        n++;
        var sha = Sha(n + 100_000);
        var filing = undated
            ? new StoredFiling(FilingId.Create(Who, FilingKind.Shareholding, period, sha), Who, FilingKind.Shareholding, period, null, BroadcastState.Unavailable, "date_only", ts, sha, 10, "s.csv", null)
            : new StoredFiling(FilingId.Create(Who, FilingKind.Shareholding, period, sha), Who, FilingKind.Shareholding, period, ts.ToUniversalTime(), BroadcastState.Ok, null, ts, sha, 10, "s.csv", null);
        return new ShareholdingEnvelope(filing, [new ShareholdingLine("promoter", Math.Round((decimal)(30 + random.NextDouble() * 40), 2), random.Next(3) == 0 ? null : Math.Round((decimal)(random.NextDouble() * 30), 2))]);
    }

    private static CalendarRow Meeting(Random random, ref int n, DateOnly meeting, DateTimeOffset? intimated)
    {
        n++;
        var sha = Sha(n + 200_000);
        var period = QuarterEnds.Back(meeting.AddDays(-30), 0);
        var filing = intimated is { } ts
            ? new StoredFiling(FilingId.Create(Who, FilingKind.BoardMeeting, period, sha), Who, FilingKind.BoardMeeting, period, ts.ToUniversalTime(), BroadcastState.Ok, null, ts, sha, 10, "m.csv", null)
            : new StoredFiling(FilingId.Create(Who, FilingKind.BoardMeeting, period, sha), Who, FilingKind.BoardMeeting, period, null, BroadcastState.Unavailable, "missing", DateTimeOffset.UnixEpoch, sha, 10, "m.csv", null);
        return new CalendarRow(filing, meeting, MeetingRow.Results, random.Next(6) == 0 ? MeetingRow.Cancelled : MeetingRow.Scheduled);
    }

    private static World Generate(Random random)
    {
        var n = 0;
        var world = new World { At = Ist0(new DateOnly(2026, 1, 5).AddDays(random.Next(0, 360)), 9 * 60 + 30 + random.Next(0, 360)) };
        for (var k = 0; k < 18; k++)
        {
            if (random.Next(10) == 0)
            {
                continue;
            }

            var period = QuarterEnds.Back(new DateOnly(2026, 9, 30), 17 - k);
            var ts = Ist0(period.AddDays(random.Next(20, 50)), random.Next(540, 1020));
            world.Results.Add(Results(random, ref n, period, ts));
            world.Holdings.Add(Holding(random, ref n, period, Ist0(period.AddDays(random.Next(12, 25)), random.Next(540, 1020))));
            var meeting = period.AddDays(random.Next(15, 40));
            world.Rows.Add(Meeting(random, ref n, meeting, Ist0(meeting.AddDays(-random.Next(2, 10)), random.Next(540, 1020))));
        }

        var price = 100.0;
        var index = 20000.0;
        for (var date = new DateOnly(2025, 12, 1); date <= new DateOnly(2027, 1, 20); date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            price *= 1 + ((random.NextDouble() - 0.5) * 0.04);
            index *= 1 + ((random.NextDouble() - 0.5) * 0.02);
            world.Index[date] = Math.Round(index, 2);
            world.Bars.Add(new Bar(Ist0(date, 555).ToUnixTimeSeconds(), price, price, price, price, 100));
            world.Bars.Add(new Bar(Ist0(date, 929).ToUnixTimeSeconds(), price, price, price, Math.Round(price * (1 + ((random.NextDouble() - 0.5) * 0.01)), 4), 100));
        }

        for (var a = random.Next(0, 3); a > 0; a--)
        {
            world.Actions.Add(new AppliedAction(new DateOnly(2024, 1, 1).AddDays(random.Next(0, 1100)), CorporateActionKind.Split, 1, 2, 0.5m));
        }

        return world;
    }

    // the day bars the store would hold: one per India date before the exclusive end, the close of the last minute bar of the date
    private static IReadOnlyList<BarRow> DailyRows(IEnumerable<Bar> minutes, DateOnly toExclusive) =>
    [
        .. minutes.OrderBy(b => b.EpochSeconds).GroupBy(b => SessionClock.DateOf(b.EpochSeconds)).Where(g => g.Key < toExclusive)
            .Select(g => new BarRow(Ist0(g.Key, 0), g.First().Open, g.Max(b => b.High), g.Min(b => b.Low), g.Last().Close, g.Sum(b => b.Volume), g.Count(), true, null)),
    ];

    private static async Task<string> EvaluateAsync(World world, string version) => (await EvaluateManyAsync(world, version, [world.At]))[0];

    // one extractor call for several decision instants: the facts are asked once per distinct set of visible filings and shared
    private static async Task<List<string>> EvaluateManyAsync(World world, string version, List<DateTimeOffset> instants, bool countFactReads = false)
    {
        var bars = Substitute.For<IBarQueries>();
        bars.ReadDailyBarsAsync(Who, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ci => ValueTask.FromResult(DailyRows(world.Bars, ci.ArgAt<DateOnly>(2))));
        var factors = Substitute.For<IAdjustmentFactors>();
        factors.GetAsync(Who, Arg.Any<CancellationToken>()).Returns(AdjustmentFactors.From(world.Actions));
        var facts = Substitute.For<IResultFactsAsOf>();
        facts.Facts(Who, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ci => ValueTask.FromResult(ResultFactsAsOfRule.Compute(world.Results, ci.ArgAt<DateTimeOffset>(1), Lag) with { ParserVersion = ResultsParser.Version }));
        var holdings = Substitute.For<IShareholdingAsOf>();
        holdings.Latest(Who, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ci => ValueTask.FromResult(ShareholdingAsOfRule.Compute(world.Holdings, ci.ArgAt<DateTimeOffset>(1), Lag)));
        var filings = world.Results.Where(r => r.Filing is { State: BroadcastState.Ok, BroadcastTs: not null }).Select(r => new ResultsFilingTime(Who, r.Filing.PeriodEnd, r.Filing.BroadcastTs!.Value)).ToList();
        var sources = new ExtensionSources(
            new TradingCalendar(),
            Lag,
            EventCalendarSnapshot.Create(world.Rows, 5),
            new Dictionary<Guid, IReadOnlyList<ResultsFilingTime>> { [Who] = filings },
            world.Index);

        var extractor = new ExtensionFeatureExtractor(bars, factors, facts, holdings);
        var result = await extractor.ExtractAsync(version, Who, [.. instants.Select((at, i) => new ExtensionDecision("t" + i, at))], sources, Ct);

        if (countFactReads)
        {
            _lastFactReads = facts.ReceivedCalls().Count();
        }

        return [.. Enumerable.Range(0, instants.Count).Select(i => string.Join("|", result["t" + i].Select(c => BitConverter.DoubleToInt64Bits(c.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + c.Reason + ":" + c.Unverified)))];
    }

    private static async Task<string> BothAsync(World world) => await EvaluateAsync(world, "ev-1") + "#" + await EvaluateAsync(world, "fu-1");

    private static List<World> Worlds(int seed)
    {
        var random = new Random(seed);
        return [.. Enumerable.Range(0, Cases).Select(_ => Generate(random))];
    }

    [Fact]
    public async Task Inputs_that_are_not_visible_at_the_decision_instant_can_be_added_changed_or_removed_without_changing_a_value()
    {
        var random = new Random(150501);
        var n = 10_000;
        var withValue = 0;
        foreach (var original in Worlds(15))
        {
            var baseline = await BothAsync(original);
            withValue += baseline.Contains("::", StringComparison.Ordinal) ? 1 : 0;
            var at = original.At;
            DateTimeOffset Later() => at - Lag + TimeSpan.FromMinutes(random.Next(1, 200 * 24 * 60));
            DateOnly Quarter() => QuarterEnds.Back(new DateOnly(2026, 9, 30), random.Next(0, 18));

            var added = original.Copy();
            added.Results.Add(Results(random, ref n, Quarter(), Later()));
            added.Results.Add(Results(random, ref n, Quarter(), Later(), undated: true));
            added.Holdings.Add(Holding(random, ref n, Quarter(), Later()));
            added.Holdings.Add(Holding(random, ref n, Quarter(), Later(), undated: true));
            added.Rows.Add(Meeting(random, ref n, DateOnly.FromDateTime(at.Date).AddDays(random.Next(-40, 90)), Later()));
            added.Rows.Add(Meeting(random, ref n, DateOnly.FromDateTime(at.Date).AddDays(random.Next(-40, 90)), null));
            (await BothAsync(added)).ShouldBe(baseline, "invisible filings, groups and rows added");

            var changed = original.Copy();
            for (var i = 0; i < changed.Results.Count; i++)
            {
                if (!Visible(changed.Results[i].Filing.BroadcastTs, at))
                {
                    changed.Results[i] = Results(random, ref n, changed.Results[i].Filing.PeriodEnd!.Value, Later());
                }
            }

            for (var i = 0; i < changed.Holdings.Count; i++)
            {
                if (!Visible(changed.Holdings[i].Filing.BroadcastTs, at))
                {
                    changed.Holdings[i] = Holding(random, ref n, changed.Holdings[i].Filing.PeriodEnd!.Value, Later());
                }
            }

            for (var i = 0; i < changed.Rows.Count; i++)
            {
                if (!Visible(changed.Rows[i].Filing.BroadcastTs, at))
                {
                    changed.Rows[i] = Meeting(random, ref n, changed.Rows[i].MeetingDate.AddDays(random.Next(-5, 6)), Later());
                }
            }

            (await BothAsync(changed)).ShouldBe(baseline, "invisible filings, groups and rows changed");

            var removed = original.Copy();
            removed.Results.RemoveAll(r => !Visible(r.Filing.BroadcastTs, at));
            removed.Holdings.RemoveAll(h => !Visible(h.Filing.BroadcastTs, at));
            removed.Rows.RemoveAll(r => !Visible(r.Filing.BroadcastTs, at));
            (await BothAsync(removed)).ShouldBe(baseline, "invisible filings, groups and rows removed");

            var later = original.Copy();
            later.Bars = [.. later.Bars.Select(b => b.EpochSeconds > at.ToUnixTimeSeconds() ? b with { Close = b.Close * 3.7, Open = 1, High = 9999, Low = 0.01 } : b), .. Enumerable.Range(1, 5).Select(d => new Bar(at.ToUnixTimeSeconds() + (d * 400_000), 1, 1, 1, 1, 1))];
            later.Actions.Add(new AppliedAction(DateOnly.FromDateTime(at.Date).AddDays(1), CorporateActionKind.Bonus, 1, 1, 0.5m));
            foreach (var date in later.Index.Keys.Where(d => d > DateOnly.FromDateTime(at.Date)).ToList())
            {
                later.Index[date] *= 1.9;
            }

            (await BothAsync(later)).ShouldBe(baseline, "bars after t, a later action and later index closes changed");
        }

        withValue.ShouldBeGreaterThan(Cases / 4, "the generated worlds must exercise real values, not only unknown ones");
    }

    [Fact]
    public async Task Changing_a_visible_input_changes_at_least_one_value()
    {
        var changedCases = 0;
        var tried = 0;
        foreach (var original in Worlds(16))
        {
            var newest = original.Holdings.Where(h => Visible(h.Filing.BroadcastTs, original.At)).OrderByDescending(h => h.Filing.PeriodEnd).FirstOrDefault();
            if (newest is null)
            {
                continue;
            }

            tried++;
            var changed = original.Copy();
            var index = changed.Holdings.IndexOf(newest);
            var line = newest.Lines[0];
            changed.Holdings[index] = newest with { Lines = [line with { Pct = (line.Pct ?? 0) + 1, PledgedPct = (line.PledgedPct ?? 0) + 7.5m }] };

            (await BothAsync(changed)).ShouldNotBe(await BothAsync(original));
            changedCases++;
        }

        tried.ShouldBeGreaterThan(Cases / 2);
        changedCases.ShouldBe(tried);

        var calendarTried = 0;
        foreach (var generated in Worlds(17))
        {
            // move the instant to the day after an intimation, so that a meeting is upcoming and visible
            var anchor = generated.Rows.FirstOrDefault(r => !r.IsCancelled && r.Filing.BroadcastTs is not null);
            if (anchor is null)
            {
                continue;
            }

            var original = generated.Copy();
            original.At = Ist0(DateOnly.FromDateTime(anchor.Filing.BroadcastTs!.Value.ToOffset(Ist).Date).AddDays(1), 10 * 60);
            var snapshot = EventCalendarSnapshot.Create(original.Rows, 5);
            var answer = snapshot.Answer(Who, original.At, new TradingCalendar());
            if (answer.Days is null || answer.MeetingDate is not { } date)
            {
                continue;
            }

            var changed = original.Copy();
            var row = changed.Rows.First(r => r.MeetingDate == date && Visible(r.Filing.BroadcastTs, original.At) && !r.IsCancelled);
            changed.Rows[changed.Rows.IndexOf(row)] = row with { MeetingDate = date.AddDays(7) };
            calendarTried++;

            (await EvaluateAsync(changed, "ev-1")).ShouldNotBe(await EvaluateAsync(original, "ev-1"));
        }

        calendarTried.ShouldBeGreaterThan(Cases / 2);
    }

    [Fact]
    public async Task Many_decisions_in_one_call_share_the_facts_read_and_give_the_values_each_would_get_alone()
    {
        var random = new Random(19);
        var reads = 0;
        var shared = 0;
        foreach (var world in Worlds(19).Take(60))
        {
            // five instants through the year, some between two filings and some after the same one
            var instants = Enumerable.Range(0, 5).Select(_ => world.At.AddDays(random.Next(-120, 120)).AddMinutes(random.Next(0, 600))).ToList();

            var together = await EvaluateManyAsync(world, "fu-1", instants, countFactReads: true);
            reads += _lastFactReads;
            var alone = new List<string>();
            foreach (var at in instants)
            {
                alone.Add((await EvaluateManyAsync(world, "fu-1", [at], countFactReads: true))[0]);
                shared += _lastFactReads;
            }

            together.ShouldBe(alone, "the cache must not change a value");
        }

        reads.ShouldBeLessThan(shared, "the facts are read once per distinct set of visible filings, not once per decision");
    }

    [Fact]
    public async Task The_order_of_the_inputs_never_matters()
    {
        var random = new Random(18);
        foreach (var original in Worlds(18))
        {
            var shuffled = original.Copy();
            shuffled.Results.Clear();
            shuffled.Results.AddRange(original.Results.OrderBy(_ => random.Next()));
            shuffled.Holdings.Clear();
            shuffled.Holdings.AddRange(original.Holdings.OrderBy(_ => random.Next()));
            shuffled.Rows.Clear();
            shuffled.Rows.AddRange(original.Rows.OrderBy(_ => random.Next()));
            shuffled.Actions.Clear();
            shuffled.Actions.AddRange(original.Actions.OrderBy(_ => random.Next()));
            shuffled.Bars = [.. original.Bars.OrderBy(_ => random.Next())];

            (await BothAsync(shuffled)).ShouldBe(await BothAsync(original));
        }
    }
}
