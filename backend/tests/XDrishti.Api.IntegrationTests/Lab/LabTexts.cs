namespace XDrishti.Api.IntegrationTests.Lab;

/// <summary>Strategy texts for the Lab tests: a valid one (written at the fixed layout of the file) with a chosen id, so that each test owns its family.</summary>
internal static class LabTexts
{
    private const string Template = """
        strategy:
          schema: 1
          id: __ID__
          style: intraday
          timeframes:
            - 15m
          sides:
            - BUY
            - SELL
          params:
            pull_atr: 0.5
            rr: 2
          setup:
            all:
              - "tf(\"1h\").ema(20) > tf(\"1h\").ema(50)"
          trigger:
            all:
              - "close > vwap and close > open"
              - "close[1] > vwap[1] and close[2] > vwap[2]"
          entry:
            type: at_close
          stop:
            type: swing_low
            lookback: 2
          exits:
            - policy: FIXED_RR
              rr: [rr]
          sell:
            setup:
              all:
                - "tf(\"1h\").ema(20) < tf(\"1h\").ema(50)"
            trigger:
              all:
                - "close < vwap and close < open"
                - "close[1] < vwap[1] and close[2] < vwap[2]"
            entry:
              type: at_close
            stop:
              type: swing_high
              lookback: 2
            exits:
              - policy: FIXED_RR
                rr: [rr]
          time_rules:
            first_entry: "10:00"
            last_entry: "14:30"
            square_off: "15:15"
        """;

    /// <summary>A fresh family id: a letter, then 12 hex characters.</summary>
    public static string NewId() => "LAB_" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>The valid text under the given id.</summary>
    public static string Valid(string id) => Template.Replace("__ID__", id, StringComparison.Ordinal);

    /// <summary>The valid text with the first trigger changed: another content, another hash.</summary>
    public static string Changed(string id, string trigger = "close > vwap and close > open and close > low") => Valid(id).Replace("close > vwap and close > open\"", trigger + "\"", StringComparison.Ordinal);
}
