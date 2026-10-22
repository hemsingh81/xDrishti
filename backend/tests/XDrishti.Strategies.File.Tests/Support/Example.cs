using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.File.Tests.Support;

/// <summary>The example of doc 11 section 2 (the nested trail corrected to <c>trail_atr_mult</c>, P3-02c D4) and the model it must read to.</summary>
internal static class Example
{
    public const string Text = """
        strategy:
          id: VWAP_RSI_TREND
          version: 1
          style: intraday
          timeframes: [15m]
          sides: [BUY, SELL]              # SELL auto-mirrored unless a `sell:` block is given
          universe: { baskets: [MyChoice, "Best stocks"] }   # or instruments: [...] or a rule-based basket
          params: { rsi_len: 14, rsi_min: 55, swing_lookback: 5 }
          setup:
            all:
              - time >= "09:45"
              - tf("1h").ema(20) > tf("1h").ema(50)
              - rsi(rsi_len) > rsi_min
          trigger:
            all:
              - crosses_above(close, vwap)
          entry: { type: at_close }       # at_close | stop_above_high | limit_pullback
          stop:  { type: swing_low, lookback: swing_lookback, buffer_atr: 0.1 }
          exits:
            - { policy: FIXED_RR, rr: [2.0] }
            - { policy: PARTIAL_TRAIL, book_pct: 50, t1_rr: 1.0, trail_atr_mult: 2.0 }
          time_rules: { last_entry: "14:30", square_off: "15:15" }
          filters: [not_event_day, min_turnover]

        test:
          period: { from: 2022-01-01, to: today }
          method: walk_forward            # walk_forward | simple_split | full_period (exploration only)
          compare_with: [baseline_random, ORB]
          robustness: { param_sensitivity: true, monte_carlo: 1000, by_year: true, by_regime: true }
        """;

    public static StrategyDefinition Model() => new(
        1,
        "VWAP_RSI_TREND",
        Style.Intraday,
        [Timeframe.M15],
        [Side.Buy, Side.Sell],
        [new StrategyParam("rsi_len", 14), new StrategyParam("rsi_min", 55), new StrategyParam("swing_lookback", 5)],
        new SideRules(
            new ConditionList(ListMode.All, ["time >= \"09:45\"", "tf(\"1h\").ema(20) > tf(\"1h\").ema(50)", "rsi(rsi_len) > rsi_min"]),
            new ConditionList(ListMode.All, ["crosses_above(close, vwap)"]),
            null,
            new EntrySpec(EntryKind.AtClose),
            new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("swing_lookback"), BufferAtr: Quantity.Of(0.1)),
            [
                new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [Quantity.Of(2)])]),
                new ExitSpec(ExitPolicy.PartialTrail, [new ExitParameter("book_pct", [Quantity.Of(50)]), new ExitParameter("t1_rr", [Quantity.Of(1)]), new ExitParameter("trail_atr_mult", [Quantity.Of(2)])]),
            ],
            null),
        null,
        new TimeRules(null, new TimeOnly(14, 30), new TimeOnly(15, 15), null),
        ["not_event_day", "min_turnover"]);

    /// <summary>The same strategy with the white space, key order, comments, number spelling, style and position of <c>version</c> changed.</summary>
    public const string Perturbed = """
        # a comment first
        strategy:
            version: 1
            filters: [not_event_day, min_turnover]
            time_rules:
                square_off: '15:15'
                last_entry: "14:30"
            exits:
              - policy: FIXED_RR
                rr:
                  - 2
              - {policy: PARTIAL_TRAIL, trail_atr_mult: 2.00, t1_rr: 1, book_pct: 50.0}
            stop: {buffer_atr: 0.10, type: swing_low, lookback: swing_lookback}
            entry:
                type: at_close
            trigger: {all: ["crosses_above(close,vwap)"]}
            setup:
              all:
                - 'time  >=  "09:45"'
                - (tf("1h").ema(20)) > tf("1h").ema(50)
                - rsi( rsi_len ) > rsi_min
            params:
                swing_lookback: 5.0
                rsi_min: 55
                rsi_len: 14
            sides:
              - BUY
              - SELL
            timeframes:
              - 15m
            style: intraday
            id: "VWAP_RSI_TREND"
        """;
}
