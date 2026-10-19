using static XDrishti.Simulation.Tests.Oracle.Fx;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>Rows F and W of spec P3-03 (the numbers are those of the exact mode; the same numbers must hold in the modelled mode).</summary>
internal static class RowsP303
{
    private static Mn B => M("09:44", 99.00m, 100.50m, 99.00m, 100.50m);

    private static Tape PDay(string to, IEnumerable<Mn> more, params string[] absent) =>
        new Tape().Day(Mon, "09:15", to, 99.00m, [M("09:20", 99.00m, 100.00m, 99.00m, 99.00m), M("09:25", 99.00m, 99.00m, 98.00m, 99.00m), .. more], absent);

    private static HandRow Row(string id, Tape t, OracleCandidate c, Expect x, OracleRules? rules = null) => new()
    {
        Id = id,
        Source = "P3-03",
        Bars = t.ToArray(),
        Candidate = c,
        Rules = rules ?? Intraday(),
        Expect = x,
    };

    private static OracleCandidate CandB(Tape t) => Cand(t, OracleSide.Buy, Mon, "09:45", 100.50m, 98.00m);

    private static Mn[] F5Minutes(bool with1515) =>
    [
        B,
        M("10:00", 100.50m, 102.00m, 100.50m, 102.00m),
        M("11:40", 102.00m, 102.00m, 99.00m, 101.50m),
        M("13:00", 101.50m, 104.50m, 101.50m, 103.00m),
        .. with1515 ? new[] { M("15:15", 103.00m, 106.00m, 97.00m, 103.00m) } : [],
    ];

    internal static IEnumerable<HandRow> All()
    {
        Tape t;

        t = PDay("15:29", [B, M("10:05", 100.50m, 100.50m, 99.50m, 100.50m), M("10:30", 100.50m, 104.50m, 100.50m, 104.00m), M("11:10", 104.00m, 105.50m, 103.50m, 105.00m)]);
        yield return Row("F1", t, CandB(t), new Expect { Entry = 100.50m, Exit = 105.50m, Reason = OracleExitReason.Target, R = 2.0m, Mae = 0.4m, Mfe = 2.0m, Target = 105.50m, Bars = 86, EntryAt = Mon + " 09:45", ExitAt = Mon + " 11:10" });

        t = PDay("15:29", [B, M("10:20", 100.50m, 101.50m, 100.50m, 101.00m), M("10:50", 101.00m, 102.00m, 97.50m, 98.50m)]);
        yield return Row("F2", t, CandB(t), new Expect { Entry = 100.50m, Exit = 98.00m, Reason = OracleExitReason.Stop, R = -1.0m, Mae = 1.0m, Mfe = 0.4m, Bars = 66, ExitAt = Mon + " 10:50" });

        t = PDay("15:29", [B, M("10:20", 100.50m, 106.00m, 97.50m, 101.00m)]);
        yield return Row("F3", t, CandB(t), new Expect { Entry = 100.50m, Exit = 98.00m, Reason = OracleExitReason.Stop, R = -1.0m, Mae = 1.0m, Mfe = 0.0m, Bars = 36, Extra = { ["AmbiguousMinutes"] = 1 } });

        t = PDay("15:29", [B, M("09:45", 100.50m, 100.50m, 97.50m, 98.50m)]);
        yield return Row("F4", t, CandB(t), new Expect { Entry = 100.50m, Exit = 98.00m, Reason = OracleExitReason.Stop, R = -1.0m, Mae = 1.0m, Mfe = 0.0m, Bars = 1, EntryAt = Mon + " 09:45", ExitAt = Mon + " 09:45" });

        t = PDay("15:29", F5Minutes(true));
        yield return Row("F5", t, CandB(t), new Expect { Entry = 100.50m, Exit = 103.00m, Reason = OracleExitReason.SquareOff, R = 1.0m, Mae = 0.6m, Mfe = 1.6m, Bars = 331, ExitAt = Mon + " 15:15" });

        t = PDay("15:09", F5Minutes(false));
        yield return Row("F6", t, CandB(t), new Expect { Entry = 100.50m, Exit = 103.00m, Reason = OracleExitReason.EndOfData, R = 1.0m, Mae = 0.6m, Mfe = 1.6m, Bars = 325, ExitAt = Mon + " 15:09" });

        t = PDay("15:29", [.. F5Minutes(false), M("15:16", 103.50m, 106.00m, 97.00m, 103.50m)], "15:10-15:15");
        yield return Row("F7", t, CandB(t), new Expect { Entry = 100.50m, Exit = 103.50m, Reason = OracleExitReason.SquareOff, R = 1.2m, Mae = 0.6m, Mfe = 1.6m, Bars = 326, ExitAt = Mon + " 15:16", Extra = { ["LateSquareOffs"] = 1 } });

        t = PDay("15:29", [M("11:29", 99.00m, 100.50m, 99.00m, 100.50m)]);
        yield return Row("F9", t, Cand(t, OracleSide.Buy, Mon, "11:30", 100.50m, 98.00m), new Expect { Entry = 100.50m, Exit = 100.50m, Reason = OracleExitReason.SquareOff, R = 0.0m, Mae = 0.0m, Mfe = 0.0m, Bars = 226, EntryAt = Mon + " 11:30", ExitAt = Mon + " 15:15" });

        t = PDay("15:29", [M("09:44", 99.00m, 99.00m, 97.50m, 97.50m), M("10:10", 97.50m, 98.50m, 97.50m, 97.50m), M("11:30", 97.50m, 97.50m, 95.00m, 96.00m), M("12:00", 96.00m, 96.00m, 92.50m, 93.00m)]);
        yield return Row("F12", t, Cand(t, OracleSide.Sell, Mon, "09:45", 97.50m, 100.00m), new Expect { Entry = 97.50m, Exit = 92.50m, Target = 92.50m, Reason = OracleExitReason.Target, R = 2.0m, Mae = 0.4m, Mfe = 2.0m, Bars = 136, ExitAt = Mon + " 12:00" });

        t = PDay("15:29", [B, M("10:05", 100.50m, 100.50m, 97.50m, 97.50m)]);
        yield return Row("F13-buy", t, CandB(t), new Expect { Entry = 100.50m, Exit = 98.00m, Reason = OracleExitReason.Stop, R = -1.0m, Mae = 1.0m, Mfe = 0.0m, Bars = 21, ExitAt = Mon + " 10:05" });
        yield return Row("F13-sell", t, Cand(t, OracleSide.Sell, Mon, "10:15", 97.50m, 100.00m), new Expect { Entry = 97.50m, Exit = 97.50m, Reason = OracleExitReason.SquareOff, R = 0.0m, Mae = 0.0m, Mfe = 0.0m, Bars = 301, EntryAt = Mon + " 10:15", ExitAt = Mon + " 15:15" });

        t = PDay("15:29", [B, M("09:45", 97.75m, 98.50m, 97.75m, 98.00m)]);
        yield return Row("F14", t, CandB(t), new Expect { Trade = false, NoTrade = OracleNoTradeReason.EntryThroughStop });

        t = PDay("15:29", [M("09:43", 99.00m, 100.50m, 99.00m, 100.50m)], "09:44");
        yield return Row("F15", t, Cand(t, OracleSide.Buy, Mon, "09:46", 100.50m, 98.00m), new Expect { Entry = 100.50m, Exit = 100.50m, Reason = OracleExitReason.SquareOff, R = 0.0m, Mae = 0.0m, Mfe = 0.0m, Bars = 330, EntryAt = Mon + " 09:46", ExitAt = Mon + " 15:15" });

        t = PDay("15:29", [M("09:44", 99.00m, 100.50m, 99.00m, 100.50m)], "09:29-09:43");
        yield return Row("F16", t, Cand(t, OracleSide.Buy, Mon, "09:45", 100.50m, 98.00m), new Expect { Entry = 100.50m, Exit = 100.50m, Reason = OracleExitReason.SquareOff, R = 0.0m, Mae = 0.0m, Mfe = 0.0m, Bars = 331, EntryAt = Mon + " 09:45" });

        Mn[] f1 = [B, M("10:05", 100.50m, 100.50m, 99.50m, 100.50m), M("10:30", 100.50m, 104.50m, 100.50m, 104.00m), M("11:10", 104.00m, 105.50m, 103.50m, 105.00m)];
        t = PDay("15:29", f1, "09:45");
        yield return Row("F17", t, CandB(t), new Expect { Entry = 100.50m, Exit = 105.50m, Reason = OracleExitReason.Target, R = 2.0m, Mae = 0.4m, Mfe = 2.0m, Bars = 85, EntryAt = Mon + " 09:46", Extra = { ["EntriesAfterMissingMinutes"] = 1 } });

        t = PDay("15:29", f1, "09:45-09:49");
        yield return Row("F17b-fill", t, CandB(t), new Expect { Entry = 100.50m, Exit = 105.50m, Reason = OracleExitReason.Target, R = 2.0m, Bars = 81, EntryAt = Mon + " 09:50", Extra = { ["EntriesAfterMissingMinutes"] = 1 } });

        t = PDay("15:29", f1, "09:45-09:50");
        yield return Row("F17b-miss", t, CandB(t), new Expect { Trade = false, NoTrade = OracleNoTradeReason.NoFillBar });

        // Walker table. The fill minute is 100.50 flat unless the row says otherwise; minutes not listed repeat the previous close.
        t = PDay("15:29", [B, M("09:45", 97.75m, 97.75m, 97.75m, 97.75m)]);
        yield return Row("W1", t, CandB(t), new Expect { Trade = false, NoTrade = OracleNoTradeReason.EntryThroughStop });

        t = PDay("15:29", [B, M("09:45", 98.00m, 98.00m, 98.00m, 98.00m)]);
        yield return Row("W2", t, CandB(t), new Expect { Trade = false, NoTrade = OracleNoTradeReason.EntryThroughStop });

        t = PDay("15:29", [B, M("09:45", 98.25m, 98.75m, 98.25m, 98.50m)]);
        yield return Row("W3", t, CandB(t), new Expect { Entry = 98.25m, Target = 98.75m, Exit = 98.75m, Reason = OracleExitReason.Target, R = 2.0m, Mae = 0.0m, Mfe = 2.0m, Bars = 1 });

        t = PDay("15:29", [M("09:45", 100.00m, 100.00m, 100.00m, 100.00m)]);
        yield return Row("W4a", t, Cand(t, OracleSide.Sell, Mon, "09:45", 99.75m, 100.00m), new Expect { Trade = false, NoTrade = OracleNoTradeReason.EntryThroughStop });

        t = PDay("15:29", [M("09:45", 99.75m, 99.75m, 99.75m, 99.75m)]);
        yield return Row("W4b", t, Cand(t, OracleSide.Sell, Mon, "09:45", 99.75m, 100.00m), new Expect { Entry = 99.75m, Target = 99.25m, Exit = 99.75m, Reason = OracleExitReason.SquareOff, R = 0.0m, Mae = 0.0m, Mfe = 0.0m, Bars = 331 });

        t = PDay("15:29", [B, M("10:00", 100.00m, 100.00m, 98.00m, 99.00m)]);
        yield return Row("W5", t, CandB(t), new Expect { Entry = 100.50m, Exit = 98.00m, Reason = OracleExitReason.Stop, R = -1.0m, Mae = 1.0m, Mfe = 0.0m, Bars = 16 });

        t = PDay("15:29", [B, M("10:00", 97.00m, 97.75m, 96.50m, 97.25m)]);
        yield return Row("W6", t, CandB(t), new Expect { Entry = 100.50m, Exit = 97.00m, Reason = OracleExitReason.Stop, R = -1.4m, Mae = 1.4m, Mfe = 0.0m, Bars = 16, Extra = { ["StopGapExits"] = 1 } });

        t = PDay("15:29", [B, M("10:00", 106.00m, 106.50m, 105.75m, 106.25m)]);
        yield return Row("W7", t, CandB(t), new Expect { Entry = 100.50m, Exit = 105.50m, Reason = OracleExitReason.Target, R = 2.0m, Mae = 0.0m, Mfe = 2.0m, Bars = 16, Extra = { ["TargetGapExits"] = 1 } });

        t = PDay("15:29", [B]);
        yield return Row("W8", t, CandB(t), new Expect { Entry = 100.50m, TargetIsNull = true, Exit = 100.50m, Reason = OracleExitReason.SquareOff, R = 0.0m, Bars = 331 }, Intraday(rr: null));

        t = PDay("09:44", [B]);
        yield return Row("W9-list-end", t, Cand(t, OracleSide.Buy, Mon, "09:45", 100.50m, 98.00m), new Expect { Trade = false, NoTrade = OracleNoTradeReason.NoFillBar });

        t = PDay("15:29", [B]).Day(Tue, "09:15", "15:29");
        yield return Row("W9-next-date", t, Cand(t, OracleSide.Buy, Mon, "15:30", 100.50m, 98.00m), new Expect { Trade = false, NoTrade = OracleNoTradeReason.NoFillBar });

        t = PDay("15:29", [B], "09:45-09:49");
        yield return Row("W9b-5min", t, CandB(t), new Expect { Entry = 100.50m, Exit = 100.50m, Reason = OracleExitReason.SquareOff, R = 0.0m, Bars = 326, EntryAt = Mon + " 09:50", Extra = { ["EntriesAfterMissingMinutes"] = 1 } });

        t = PDay("15:29", [B], "09:45-09:50");
        yield return Row("W9b-6min", t, CandB(t), new Expect { Trade = false, NoTrade = OracleNoTradeReason.NoFillBar });

        t = PDay("15:29", [B]);
        yield return Row("W10", t, Cand(t, OracleSide.Buy, Mon, "15:15", 100.50m, 98.00m), new Expect { Trade = false, NoTrade = OracleNoTradeReason.AfterSquareOff });

        t = PDay("15:29", [B, M("10:00", 106.00m, 106.50m, 97.50m, 100.00m)]);
        yield return Row("W13", t, CandB(t), new Expect { Entry = 100.50m, Exit = 105.50m, Reason = OracleExitReason.Target, R = 2.0m, Mae = 0.0m, Mfe = 2.0m, Bars = 16, Extra = { ["TargetGapExits"] = 1 } });

        t = PDay("15:29", [B, M("15:15", 97.50m, 97.75m, 97.00m, 97.25m)]);
        yield return Row("W14-stop", t, CandB(t), new Expect { Entry = 100.50m, Exit = 97.50m, Reason = OracleExitReason.Stop, R = -1.2m, Mae = 1.2m, Mfe = 0.0m, Bars = 331, Extra = { ["StopGapExits"] = 1 } });

        t = PDay("15:29", [B, M("15:15", 106.00m, 106.50m, 105.75m, 106.25m)]);
        yield return Row("W14-target", t, CandB(t), new Expect { Entry = 100.50m, Exit = 105.50m, Reason = OracleExitReason.Target, R = 2.0m, Mae = 0.0m, Mfe = 2.0m, Bars = 331, Extra = { ["TargetGapExits"] = 1 } });
    }
}
