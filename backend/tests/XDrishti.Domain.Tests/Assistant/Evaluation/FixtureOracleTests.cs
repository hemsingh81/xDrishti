using System.Text;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>
/// Spec P9-09 AC 1: the fixture file holds exactly the records of the spec, and the P8-05 report engine, fed the fixture trades, gives the hand-worked answers of the questions within 1e-9. The engine is the one the assistant's <c>get_report</c> calls, so engine agreement
/// is not independent proof of the engine: the hand numbers of the spec are the second derivation, and this test checks the two against each other. The portfolio, plan and learning answers are closed-form arithmetic over the fixture. DQ-15 asks for the unrealised return, not
/// the XIRR of the spec's table: the tool values a holding at today's date, so an XIRR of 21.0 % holds only on the fixture date and would drift with every run (ADR 0143).
/// </summary>
public sealed class FixtureOracleTests
{
    private static readonly EvalFixture Fixture = EvalFiles.Fixture();

    private static ReportOptions Options => new() { Stats = new CellStatsOptions(PriorStrength: 0, MinTrades: 3), MinWins = 1, MinNonWins = 1 };

    private static (IReadOnlyList<ReportRun> Runs, IReadOnlyList<ReportTrade> Trades) Inputs(params string[] strategies)
    {
        var runs = Fixture.Runs.Where(r => strategies.Length == 0 || strategies.Contains(r.Strategy)).ToList();
        var reportRuns = runs.Select(r => new ReportRun(r.Key, r.Universe, r.WindowStart, r.WindowEnd, r.Style, r.Exit, false)).ToList();
        var trades = runs.SelectMany(r => r.Trades.Select((t, i) => new ReportTrade(
            r.Key, $"{r.Strategy}|{t.Symbol}|{t.Side}|{t.Entry:yyyy-MM-dd}", r.Strategy, r.Timeframe, t.Side, new Guid(Encoding.UTF8.GetBytes(t.Symbol.PadRight(16, '_'))[..16]), t.Symbol,
            new ValueList<string>(Fixture.Baskets.Where(b => b.Symbols.Contains(t.Symbol)).Select(b => b.Name)), t.Entry, t.Entry, t.R, null, 1, Ordinal: i))).ToList();
        return (reportRuns, trades);
    }

    private static ReportGrouping Group(string[] strategies, params ReportDimension[] dimensions)
    {
        var (runs, trades) = Inputs(strategies);
        var population = ReportPopulation.Create(runs, trades, Options);
        population.IsSuccess.ShouldBeTrue(population.IsFailure ? population.Error.Message : string.Empty);
        var grouping = population.Value.GroupBy(dimensions);
        grouping.IsSuccess.ShouldBeTrue(grouping.IsFailure ? grouping.Error.Message : string.Empty);
        return grouping.Value;
    }

    private static ReportGroup One(ReportGrouping grouping, string key) => grouping.Groups.Single(g => g.Key.Text == key);

    private static double Value(Metric metric)
    {
        metric.State.ShouldBe(MetricState.Value);
        return metric.Number!.Value;
    }

    [Fact]
    public void The_fixture_has_the_records_of_the_spec()
    {
        Fixture.FixtureDate.ShouldBe(new DateOnly(2026, 3, 3));
        Fixture.Instruments.Select(i => i.Symbol).ShouldBe(["EVA", "EVB", "EVC", "EVD", "EVE", "EVF", "EVH1", "EVH2"]);
        Fixture.Runs.Select(r => (r.Key, r.Trades.Count)).ShouldBe([("EVAL-ORB-15m", 24), ("EVAL-VWAP-15m", 16)]);
        Fixture.Runs[0].Trades.Max(t => t.R).ShouldBe(3.0);
        Fixture.Runs[0].Trades.Single(t => t.R == 3.0).Symbol.ShouldBe("EVC");
        Fixture.Plan.Primary.Select(t => (t.Rank, t.Symbol, t.Side, t.AllocationShares)).ShouldBe([(1, "EVA", "BUY", 200), (2, "EVD", "SELL", 150), (3, "EVC", "BUY", 100)]);
        Fixture.Plan.Reserve.ShouldBe(["EVB"]);
        Fixture.Plan.Rejected.Select(r => r.Symbol).ShouldBe(["EVE", "EVF"]);
        Fixture.CellStats.Windows.Select(w => (w.Window, w.N)).ShouldBe([("all", 12), ("250d", 12), ("60d", 12)]);
        Fixture.Learning.Lessons.Count(l => l.Status == "open").ShouldBe(2);
        Fixture.Learning.Hypotheses.Select(h => h.Status).ShouldBe(["proposed", "approved", "rejected"]);
        Fixture.Instruments.ShouldAllBe(i => !i.Name.Contains("Reliance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_hash_is_the_sha256_of_the_file_and_changes_with_one_character()
    {
        var text = EvalFiles.Text("fixture/fixture.json");

        var hash = EvalFixture.HashOf(text);

        hash.Length.ShouldBe(64);
        hash.ShouldBe(EvalFixture.HashOf(text));
        EvalFixture.HashOf(text + " ").ShouldNotBe(hash);
    }

    [Theory]
    [InlineData("""{"fixtureDate":"2026-03-03"}""")]
    [InlineData("nope")]
    public void A_file_that_is_not_a_fixture_is_refused(string json) => EvalFixtureReader.Parse(json).Error.Code.ShouldBe(EvalFixtureReader.InvalidCode);

    [Fact]
    public void A_trade_of_an_unknown_symbol_or_a_repeated_signal_is_refused()
    {
        var text = EvalFiles.Text("fixture/fixture.json");
        var runs = text.IndexOf("\"runs\"", StringComparison.Ordinal);
        var firstTrade = text.IndexOf("\"symbol\": \"EVA\"", runs, StringComparison.Ordinal);
        var unknown = text[..firstTrade] + "\"symbol\": \"EVZ\"" + text[(firstTrade + "\"symbol\": \"EVA\"".Length)..];

        EvalFixtureReader.Parse(unknown).IsFailure.ShouldBeTrue();
        var entry = text.IndexOf("\"entry\": \"2026-01-05\"", runs, StringComparison.Ordinal);
        var repeated = text[..entry] + "\"entry\": \"2026-01-06\"" + text[(entry + "\"entry\": \"2026-01-05\"".Length)..];
        EvalFixtureReader.Parse(repeated).Error.Message.ShouldBe("two trades are the same signal");
    }

    [Fact]
    public void The_engine_gives_the_hand_worked_answers_of_the_kpi_and_group_questions()
    {
        var orb = One(Group(["ORB"]), "all");
        var orbSides = Group(["ORB"], ReportDimension.Side);
        var vwap = One(Group(["VWAP_PULLBACK"]), "all");
        var baskets = Group([], ReportDimension.Basket);
        var orbGroups = Group(["ORB"], ReportDimension.Basket, ReportDimension.Side);
        var vwapBeta = One(Group(["VWAP_PULLBACK"], ReportDimension.Basket), "Basket=EvalBeta");
        var strategies = Group([], ReportDimension.Strategy);

        var actual = new Dictionary<string, double>
        {
            ["DQ-01"] = orb.N,
            ["DQ-02"] = Value(orb.WinRate) * 100,
            ["DQ-03"] = Value(orb.TotalR),
            ["DQ-04"] = Value(orb.ExpectancyR),
            ["DQ-05"] = Value(orb.ProfitFactor),
            ["DQ-06"] = Value(orb.AvgWinR),
            ["DQ-08"] = Value(vwap.WinRate) * 100,
            ["DQ-09"] = Value(One(baskets, "Basket=EvalAlpha").TotalR),
            ["DQ-10"] = Value(One(baskets, "Basket=EvalBeta").TotalR),
            ["DQ-12"] = Value(One(orbSides, "Side=SELL").TotalR),
            ["DQ-14"] = vwapBeta.N,
        };

        var expected = Expected();
        foreach (var (id, value) in actual)
        {
            value.ShouldBe(expected[id], 1e-9, id);
        }

        orb.Wins.ShouldBe(11);
        Value(orb.TotalR).ShouldBe(4.0, 1e-9);
        Fixture.Runs[0].Trades.Where(t => t.R > 0).Sum(t => t.R).ShouldBe(17.0, 1e-9);
        Fixture.Runs[0].Trades.Count(t => t.R < 0).ShouldBe(13);
        Value(One(baskets, "Basket=EvalAlpha").ExpectancyR).ShouldBe(0.275, 1e-9);
        Value(One(baskets, "Basket=EvalBeta").ExpectancyR).ShouldBe(-0.025, 1e-9);
        Value(One(orbSides, "Side=BUY").TotalR).ShouldBe(1.5, 1e-9);
        Value(One(strategies, "Strategy=VWAP_PULLBACK").TotalR).ShouldBe(1.0, 1e-9);
        One(strategies, "Strategy=ORB").N.ShouldBe(24);
        One(strategies, "Strategy=VWAP_PULLBACK").N.ShouldBe(16);
        One(strategies, "Strategy=VWAP_PULLBACK").Wins.ShouldBe(7);
        var best = orbGroups.Groups.Where(g => Value(g.TotalR) == 3.0).Select(g => g.Key.Text).Order().ToList();
        best.ShouldBe(["Basket=EvalAlpha|Side=BUY", "Basket=EvalBeta|Side=SELL"]);
        orbGroups.Groups.Count(g => Value(g.TotalR) >= 3.0).ShouldBe(2);
    }

    [Fact]
    public void The_cell_of_the_plan_is_the_twelve_buy_trades_and_a_two_trade_group_is_insufficient()
    {
        var orbBuy = One(Group(["ORB"], ReportDimension.Side), "Side=BUY");
        var evaSell = One(Group(["ORB"], ReportDimension.Instrument, ReportDimension.Side), "Instrument=EVA|Side=SELL");

        orbBuy.N.ShouldBe(Fixture.Plan.CellTrades);
        orbBuy.N.ShouldBe(12);
        evaSell.N.ShouldBe(2);
        evaSell.WinRate.State.ShouldBe(MetricState.Insufficient);
    }

    [Fact]
    public void The_portfolio_plan_and_learning_answers_are_closed_form_over_the_fixture()
    {
        var holdings = Fixture.Portfolio.Holdings;
        var one = holdings.Single(h => h.Symbol == "EVH1");
        var two = holdings.Single(h => h.Symbol == "EVH2");
        var invested = one.Shares * one.BuyPrice;
        var unrealisedPercent = (double)((one.Shares * one.LastPrice) - invested) / (double)invested * 100;

        var actual = new Dictionary<string, double>
        {
            ["DQ-15"] = unrealisedPercent,
            ["DQ-16"] = (double)invested,
            ["DQ-17"] = (double)(two.Shares * (two.LastPrice - two.BuyPrice)),
            ["DQ-18"] = (double)holdings.Sum(h => h.Shares * h.LastPrice),
            ["DQ-19"] = holdings.Count,
            ["DQ-20"] = Fixture.Plan.Primary.Count,
            ["DQ-22"] = Fixture.Plan.Primary.Single(t => t.Symbol == "EVA").AllocationShares,
            ["DQ-23"] = Fixture.CellStats.Windows.Single(w => w.Window == "all").N,
            ["DQ-24"] = Fixture.Learning.Lessons.Count(l => l.Status == "open"),
            ["DQ-25"] = Fixture.Learning.Hypotheses.Count(h => h.Status is "proposed" or "approved"),
        };

        var expected = Expected();
        foreach (var (id, value) in actual)
        {
            value.ShouldBe(expected[id], 1e-9, id);
        }

        Fixture.Plan.Primary.Single(t => t.Rank == 2).Symbol.ShouldBe("EVD");
        Fixture.Plan.Primary.Single(t => t.Rank == 2).Side.ShouldBe("SELL");
        Fixture.Runs.First(r => r.Strategy == "ORB").Trades.Max(t => t.R).ShouldBe(3.0);
    }

    private static Dictionary<string, double> Expected()
    {
        var map = new Dictionary<string, double>();
        foreach (var q in EvalFiles.DataQuestions())
        {
            switch (q.Parts[0])
            {
                case NumberPart n:
                    map[q.Id] = n.Value.ToDouble();
                    break;
                case IntegerPart i:
                    map[q.Id] = i.Value;
                    break;
                default:
                    break;
            }
        }

        return map;
    }
}
