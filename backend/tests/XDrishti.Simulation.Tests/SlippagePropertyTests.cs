using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 18: over the 500 seeded days, slippage never changes a decision, never improves a fill, and is monotone in each parameter. Each walk is made with no slippage, with the doc 16 defaults and with a ladder in which
/// each parameter in turn takes four increasing values while the others stay at the defaults.
/// </summary>
public sealed class SlippagePropertyTests
{
    private const double Tolerance = 1e-12;

    private static readonly double[] Ticks = [0, 1, 2, 3];

    private static readonly double[] RangeFractions = [0, 0.05, 0.1, 0.2];

    private static readonly double[] ExtraTicks = [0, 2, 4, 5];

    private static readonly FillRules None = FillRules.Create(0.1, SlippageConfig.None).Value;

    /// <summary>The ladders: each is four configurations in which one parameter increases and the other two are the defaults (1 tick, 0.05 of the range, 2 extra ticks).</summary>
    private static readonly (string Parameter, FillRules[] Rules)[] Ladders =
    [
        ("Ticks", [.. Ticks.Select(v => WalkProperties.Rules(v, 0.05, 2))]),
        ("BarRangeFrac", [.. RangeFractions.Select(v => WalkProperties.Rules(1, v, 2))]),
        ("StopExitExtraTicks", [.. ExtraTicks.Select(v => WalkProperties.Rules(1, 0.05, v))]),
    ];

    private static readonly List<DayWalk> Exact = WalkProperties.WalkAll(SeededDays.All, None);

    [Fact]
    public void The_trades_the_reasons_and_the_exact_prices_are_the_same_in_every_configuration()
    {
        var configurations = new List<FillRules> { FillRules.Default };
        configurations.AddRange(Ladders.SelectMany(l => l.Rules));

        foreach (var rules in configurations)
        {
            var modelled = WalkProperties.WalkAll(SeededDays.All, rules);

            modelled.Count.ShouldBe(Exact.Count);
            for (var i = 0; i < Exact.Count; i++)
            {
                Decision(modelled[i]).ShouldBe(Decision(Exact[i]), $"seed {Exact[i].Day.Seed} candidate {i} under {rules.ToCanonicalJson()}");
            }
        }
    }

    [Fact]
    public void No_fill_is_ever_better_than_the_exact_price_so_filled_r_is_never_above_exact_r()
    {
        var compared = 0;
        foreach (var walk in WalkProperties.WalkAll(SeededDays.All, FillRules.Default).Where(w => w.Trade is not null))
        {
            var trade = walk.Trade!;
            var buy = trade.Side == Side.Buy;
            var label = $"seed {walk.Day.Seed} {trade.Side} {trade.ExitReason}";

            (buy ? trade.EntryFilledPrice >= trade.EntryPrice : trade.EntryFilledPrice <= trade.EntryPrice).ShouldBeTrue(label + " entry");
            (buy ? trade.ExitFilledPrice <= trade.ExitPrice : trade.ExitFilledPrice >= trade.ExitPrice).ShouldBeTrue(label + " exit");
            trade.RFilled.ShouldBeLessThanOrEqualTo(trade.RExact + Tolerance, label);
            trade.SlippageR.ShouldBeLessThanOrEqualTo(Tolerance, label);
            trade.Fills.ShouldBe(FillBasis.Modelled, label);
            compared++;
        }

        compared.ShouldBeGreaterThanOrEqualTo(1_500);
    }

    [Fact]
    public void Raising_one_parameter_never_raises_filled_r()
    {
        var compared = 0;
        foreach (var (parameter, rules) in Ladders)
        {
            var runs = rules.Select(r => WalkProperties.WalkAll(SeededDays.All, r)).ToList();
            for (var step = 1; step < runs.Count; step++)
            {
                for (var i = 0; i < Exact.Count; i++)
                {
                    if (runs[step][i].Trade is not { } higher || runs[step - 1][i].Trade is not { } lower)
                    {
                        continue;
                    }

                    higher.RFilled.ShouldBeLessThanOrEqualTo(lower.RFilled + Tolerance, $"{parameter} step {step}, seed {Exact[i].Day.Seed} candidate {i}");
                    compared++;
                }
            }
        }

        compared.ShouldBeGreaterThanOrEqualTo(3 * 3 * 1_500);
    }

    [Fact]
    public void Zero_slippage_is_the_exact_layer_and_default_slippage_strictly_costs_something_on_most_trades()
    {
        var withNone = Exact.Where(w => w.Trade is not null).ToList();
        var costly = WalkProperties.WalkAll(SeededDays.All, FillRules.Default).Where(w => w.Trade is not null && w.Trade.SlippageR < 0).Count();

        withNone.ShouldAllBe(w => w.Trade!.Fills == FillBasis.Exact && w.Trade.EntryFilledPrice == w.Trade.EntryPrice && w.Trade.ExitFilledPrice == w.Trade.ExitPrice && w.Trade.RFilled == w.Trade.RExact && w.Trade.SlippageR == 0);
        costly.ShouldBeGreaterThan(withNone.Count / 2, "the default slippage must change something, or the properties above compare nothing");
    }

    /// <summary>What a walk decided, with the filled layer blanked: the outcome's flags, the counters' inputs and the trade as the exact layer renders it.</summary>
    private static string Decision(DayWalk walk)
    {
        var outcome = walk.Outcome;
        var trade = outcome.Trade is { } t
            ? t with { Fills = FillBasis.Exact, RFilled = 0, SlippageR = 0, EntryFilledPrice = 0, ExitFilledPrice = 0 }
            : null;
        return OutcomeFormatter.Render(XDrishti.Domain.Common.Result.Success(outcome with { Trade = trade }));
    }
}
