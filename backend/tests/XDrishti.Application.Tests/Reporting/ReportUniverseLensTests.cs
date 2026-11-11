using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>
/// BK-11b criteria 11 to 14: the <c>universe</c> lens gives each trade the tier of its instrument on its decision date (the function and date of the cell statistics), refuses with the reason when no tier can be made, is masked below 30 trades like every
/// lens, is never made for a training simulation, and a later bar never changes a past trade's tier.
/// </summary>
public sealed class ReportUniverseLensTests
{
    private static readonly DateOnly Day = ReportWorld.Day0.AddDays(100);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IUniverseSeriesSource _source = Substitute.For<IUniverseSeriesSource>();

    private static Guid Share(int n) => new(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);

    /// <summary>Shares 1 to <paramref name="shares"/>, each with a daily session from 100 days before the decision day up to the day before <paramref name="until"/>; a higher number trades less.</summary>
    private void Series(DateOnly until, int shares = 36)
    {
        _source.ReadAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var to = call.ArgAt<DateOnly>(1);
            var map = new Dictionary<Guid, IReadOnlyList<SessionValue>>();
            for (var i = 1; i <= shares; i++)
            {
                map[Share(i)] = [.. Enumerable.Range(0, 200).Select(d => Day.AddDays(d - 120)).TakeWhile(d => d < to && d < until).Select(d => new SessionValue(d, 1000 - i))];
            }

            return (IReadOnlyDictionary<Guid, IReadOnlyList<SessionValue>>)map;
        });
    }

    private static ReportWorld World(params (int Share, int Count)[] trades)
    {
        var world = new ReportWorld();
        world.AddRun('a');
        var n = 0;
        foreach (var (share, count) in trades)
        {
            for (var i = 0; i < count; i++)
            {
                world.Trades.Add(new ReportTrade(ReportWorld.Key('a'), "ORB|S" + share + "|BUY|" + n.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), "ORB", "15m", "BUY", Share(share), "S" + share, new ValueList<string>([]), Day, Day, i % 2 == 0 ? 1 : -0.5, null, 1));
                n++;
            }
        }

        world.Runs[0] = world.Runs[0] with { ClosedTrades = n, WindowStart = Day.AddDays(-5), WindowEnd = Day.AddDays(5) };
        return world;
    }

    private ReportLoader Loader(ReportWorld world, bool withSource = true) =>
        new(world, world.Training, world.Jobs, world, world, world.TrainingSettings, world.Gate, new FakeTimeProvider(ReportWorld.T0), NullLogger<ReportLoader>.Instance, universeSource: withSource ? _source : null);

    private static ReportQuery Q(string lens) => ReportWorld.Query(new ReportQueryInput(Lens: lens));

    [Fact]
    public async Task Each_trade_is_bucketed_by_the_tier_of_its_share_and_unknown_is_always_shown_with_its_count_while_small_buckets_are_masked()
    {
        Series(Day.AddDays(1));
        var world = World((1, 40), (18, 35), (36, 5), (99, 3));   // share 99 has no session: unknown

        var view = (await new GetReportGroupsHandler(Loader(world)).HandleAsync(new GetReportGroupsQuery(Q("universe")), Ct)).Value;

        view.Dimensions.ShouldBe(["universe"]);
        var byTier = view.Groups.ToDictionary(g => g.Parts[0].Value!, g => g);
        byTier.Keys.Order().ShouldBe(["liq_high", "liq_low", "liq_mid", "unknown"]);
        byTier["liq_high"].N.ShouldBe(40);
        byTier["liq_mid"].N.ShouldBe(35);
        byTier["liq_low"].N.ShouldBe(5);
        byTier["unknown"].N.ShouldBe(3);
        byTier["liq_high"].ExpectancyR.Number.ShouldNotBeNull();
        byTier["liq_low"].ExpectancyR.Number.ShouldBeNull("under 30 trades is masked by the server");
        byTier["unknown"].ExpectancyR.Number.ShouldBeNull();
    }

    [Fact]
    public async Task A_trades_report_tier_equals_its_cell_tier_the_same_function_and_date()
    {
        Series(Day.AddDays(1));
        var world = World((1, 40), (18, 35), (36, 5));
        var tagged = await UniverseTagger.TagAsync(_source, [.. world.Trades.Select(t => (t.EntryDate, t.InstrumentId))], new UniverseTierOptions(), Ct);
        var view = (await new GetReportGroupsHandler(Loader(world)).HandleAsync(new GetReportGroupsQuery(Q("universe")), Ct)).Value;

        foreach (var tier in new[] { UniverseTier.High, UniverseTier.Mid, UniverseTier.Low })
        {
            view.Groups.Single(g => g.Parts[0].Value == tier.CellPart()).N.ShouldBe(tagged.Count(t => t == tier), "the report and the cell statistics count one tier alike");
        }
    }

    [Fact]
    public async Task With_no_tier_source_or_too_few_shares_the_dimension_is_refused_with_the_reason()
    {
        Series(Day.AddDays(1), shares: 20);   // under the 30 eligible shares of the rule on every date
        var world = World((1, 40), (18, 35));

        var tooFew = await new GetReportGroupsHandler(Loader(world)).HandleAsync(new GetReportGroupsQuery(Q("universe")), Ct);
        var noSource = await new GetReportGroupsHandler(Loader(world, withSource: false)).HandleAsync(new GetReportGroupsQuery(Q("universe")), Ct);

        foreach (var refused in new[] { tooFew, noSource })
        {
            refused.Error.Code.ShouldBe("reports.dimension_unavailable");
            refused.Error.Message.ShouldBe("no liquidity tier for these trades");
        }
    }

    [Fact]
    public async Task Regime_and_account_keep_their_refusals_and_a_report_without_the_lens_reads_no_tier()
    {
        var world = World((1, 40));

        (await new GetReportGroupsHandler(Loader(world)).HandleAsync(new GetReportGroupsQuery(Q("regime")), Ct)).Error.Message.ShouldContain("Regime is not a report dimension yet");
        (await new GetReportGroupsHandler(Loader(world)).HandleAsync(new GetReportGroupsQuery(Q("account")), Ct)).Error.Message.ShouldContain("Account is unavailable");
        var plain = await new GetReportGroupsHandler(Loader(world)).HandleAsync(new GetReportGroupsQuery(Q("strategy")), Ct);

        plain.IsSuccess.ShouldBeTrue();
        await _source.DidNotReceive().ReadAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_training_simulation_report_refuses_the_universe_lens_with_the_reason_and_reads_no_tier()
    {
        var world = World((1, 40));
        var query = ReportWorld.Query(new ReportQueryInput(Lens: "universe", Purpose: "training"));

        var refused = await new GetReportGroupsHandler(Loader(world)).HandleAsync(new GetReportGroupsQuery(query), Ct);

        refused.Error.Code.ShouldBe("reports.dimension_unavailable");
        refused.Error.Message.ShouldContain("not tagged by liquidity tier");
        await _source.DidNotReceive().ReadAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Appending_sessions_on_or_after_the_decision_date_changes_no_trades_tier_the_truncation_property()
    {
        var world = World((1, 40), (18, 35), (36, 40));
        Series(Day);   // only sessions before the decision day
        var before = (await new GetReportGroupsHandler(Loader(world)).HandleAsync(new GetReportGroupsQuery(Q("universe")), Ct)).Value.Groups.Select(g => (g.Key, g.N)).ToList();

        Series(Day.AddDays(60));   // the same shares keep trading after the decision day (and trade differently)
        var after = (await new GetReportGroupsHandler(Loader(world)).HandleAsync(new GetReportGroupsQuery(Q("universe")), Ct)).Value.Groups.Select(g => (g.Key, g.N)).ToList();

        after.ShouldBe(before);
        before.Count.ShouldBeGreaterThan(1);
    }

    [Fact(Explicit = true)]
    public async Task Tagging_twenty_thousand_report_trades_takes_under_two_seconds()
    {
        Series(Day.AddDays(60));
        var trades = Enumerable.Range(0, 20_000).Select(i => (Day.AddDays(i % 40), Share((i % 36) + 1))).ToList();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var tagged = await UniverseTagger.TagAsync(_source, trades, new UniverseTierOptions(), Ct);

        watch.Stop();
        tagged.Count.ShouldBe(20_000);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }
}
