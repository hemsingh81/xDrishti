using static XDrishti.Simulation.Tests.Oracle.Fx;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>Rows M, fixture R and Q2 of spec P3-03d, and rows O of spec P3-03e.</summary>
internal static class RowsSwing
{
    private static Mn A => M("10:00", 100.00m, 100.30m, 99.80m, 100.20m);

    private static HandRow Row(string id, string source, Tape t, OracleCandidate c, OracleRules r, Expect x) => new()
    {
        Id = id,
        Source = source,
        Bars = t.ToArray(),
        Candidate = c,
        Rules = r,
        Expect = x,
    };

    private static Tape Mond(params Mn[] extra) => new Tape().Day(Mon, "09:15", "15:29", 100.00m, [A, .. extra]);

    private static OracleCandidate SetupA(Tape t) => Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m);

    private static Expect MX(decimal exit, decimal exitF, OracleExitReason reason, decimal r, decimal rf, decimal slip, decimal mae, decimal mfe, int held, int unseen, int? bars, Dictionary<string, int>? extra = null) =>
        new()
        {
            Entry = 100.00m,
            EntryF = 100.10m,
            Exit = exit,
            ExitF = exitF,
            Reason = reason,
            R = r,
            RF = rf,
            SlipR = slip,
            Mae = mae,
            Mfe = mfe,
            Held = held,
            Unseen = unseen,
            Bars = bars,
            Extra = extra ?? [],
        };

    private static Tape Fixture()
    {
        return new Tape()
            .Day(Mon, "09:15", "15:29", 99.00m, [M("09:20", 99.00m, 100.00m, 99.00m, 99.00m), M("09:25", 99.00m, 99.00m, 98.00m, 99.00m), M("10:30", 99.50m, 101.50m, 99.50m, 101.50m)])
            .Day(Tue, "09:15", "15:29")
            .Day(Wed, "09:15", "15:29", listed: [M("15:29", 101.60m, 101.80m, 101.50m, 101.70m)])
            .Day(Thu, "09:15", "15:29")
            .Day(Fri, "09:15", "15:29");
    }

    internal static IEnumerable<HandRow> All()
    {
        Tape t;

        t = Mond().Day(Tue, "09:15", "15:29", listed: [M("09:15", 98.60m, 98.90m, 98.40m, 98.50m)]);
        yield return Row("M1", "P3-03d", t, SetupA(t), Swing(5), MX(98.60m, 98.40m, OracleExitReason.Stop, -1.4m, -1.7m, -0.3m, 1.4m, 0.3m, 2, 0, 331, new() { ["StopGapExits"] = 1, ["OvernightStopGaps"] = 1 }));

        t = Mond().Day(Tue, "09:15", "15:29", listed: [M("09:15", 98.62m, 98.90m, 98.40m, 98.50m)]);
        yield return Row("M1b", "P3-03d", t, SetupA(t), Swing(5), MX(98.62m, 98.40m, OracleExitReason.Stop, -1.38m, -1.7m, -0.32m, 1.38m, 0.3m, 2, 0, null, new() { ["StopGapExits"] = 1, ["OvernightStopGaps"] = 1 }));

        t = Mond().Day(Tue, "09:15", "15:29", listed: [M("11:00", 100.00m, 100.00m, 99.10m, 99.40m)]).Day(Wed, "09:15", "15:29", listed: [M("10:40", 99.60m, 102.10m, 99.50m, 102.00m)]);
        yield return Row("M2", "P3-03d", t, SetupA(t), Swing(5), MX(102.00m, 102.00m, OracleExitReason.Target, 2.0m, 1.9m, -0.1m, 0.9m, 2.0m, 3, 0, 791));

        Tape M3Tape() => Mond().Day(Tue, "09:15", "15:29", listed: [M("10:30", 100.20m, 101.20m, 100.10m, 101.00m)]).Day(Wed, "09:15", "15:29", listed: [M("15:29", 100.60m, 100.80m, 100.50m, 100.70m)]);
        t = M3Tape();
        yield return Row("M3", "P3-03d", t, SetupA(t), Swing(3), MX(100.70m, 100.60m, OracleExitReason.MaxHold, 0.7m, 0.5m, -0.2m, 0.2m, 1.2m, 3, 0, 1080));
        yield return Row("M3b", "P3-03d", t, SetupA(t), Swing(2), MX(101.00m, 100.95m, OracleExitReason.MaxHold, 1.0m, 0.85m, -0.15m, 0.2m, 1.2m, 2, 0, 705));
        yield return Row("M3c", "P3-03d", t, SetupA(t), Swing(1), MX(100.20m, 100.15m, OracleExitReason.MaxHold, 0.2m, 0.05m, -0.15m, 0.2m, 0.3m, 1, 0, 330));

        Tape M4Tape() => Mond().Day(Wed, "09:15", "15:29", listed: [M("15:29", 100.20m, 100.40m, 100.10m, 100.30m)]).Day(Thu, "09:15", "15:29", listed: [M("15:29", 100.30m, 100.50m, 100.20m, 100.40m)]);
        t = M4Tape();
        yield return Row("M4a", "P3-03d", t, SetupA(t), Swing(3, Cal(["2026-03-03"])), MX(100.40m, 100.30m, OracleExitReason.MaxHold, 0.4m, 0.2m, -0.2m, 0.2m, 0.5m, 3, 0, 1080));
        yield return Row("M4b", "P3-03d", t, SetupA(t), Swing(3), MX(100.30m, 100.20m, OracleExitReason.MaxHold, 0.3m, 0.1m, -0.2m, 0.2m, 0.4m, 3, 1, 705));

        t = Mond().Day(Tue, "09:15", "15:29");
        yield return Row("M5", "P3-03d", t, SetupA(t), Swing(5), MX(100.20m, 100.15m, OracleExitReason.EndOfData, 0.2m, 0.05m, -0.15m, 0.2m, 0.3m, 2, 0, 705));

        t = Mond().Day(Tue, "09:15", "15:29").Day(Wed, "09:15", "15:29", listed: [M("15:29", 100.60m, 100.80m, 98.90m, 99.20m)]);
        yield return Row("M6", "P3-03d", t, SetupA(t), Swing(3), MX(99.00m, 98.75m, OracleExitReason.Stop, -1.0m, -1.35m, -0.35m, 1.0m, 0.3m, 3, 0, 1080));

        Mn[] wed7 = [M("15:28", 100.50m, 100.55m, 100.45m, 100.50m), V0("15:29", 100.60m, 100.80m, 100.50m, 100.70m)];
        t = Mond().Day(Tue, "09:15", "15:29", listed: [M("10:30", 100.20m, 101.20m, 100.10m, 101.00m)]).Day(Wed, "09:15", "15:29", listed: wed7).Day(Thu, "09:15", "15:29");
        yield return Row("M7a", "P3-03d", t, SetupA(t), Swing(3), MX(100.50m, 100.40m, OracleExitReason.MaxHold, 0.5m, 0.3m, -0.2m, 0.2m, 1.2m, 3, 0, 1079, new() { ["MaxHoldAtLastClose"] = 1 }));

        t = Mond().Day(Tue, "09:15", "15:29", listed: [M("10:30", 100.20m, 101.20m, 100.10m, 101.00m)]).Day(Wed, "09:15", "15:29", listed: wed7);
        yield return Row("M7b", "P3-03d", t, SetupA(t), Swing(3), MX(100.50m, 100.40m, OracleExitReason.EndOfData, 0.5m, 0.3m, -0.2m, 0.2m, 1.2m, 3, 0, 1079));

        // Fixture R: the 1h swing candidate decided Monday 11:15, trigger close 101.50, stop 98.00 (the low of the first hour), N = 3.
        t = Fixture();
        OracleCandidate r = Cand(t, OracleSide.Buy, Mon, "11:15", 101.50m, 98.00m);
        yield return Row("R", "P3-03d", t, r, Swing(3), new Expect { Entry = 101.50m, EntryF = 101.55m, Target = 108.50m, Exit = 101.70m, ExitF = 101.60m, Reason = OracleExitReason.MaxHold, R = 0.0571m, RF = 0.0143m, SlipR = -0.0429m, Mae = 0.0m, Mfe = 0.0857m, Held = 3 });

        t = new Tape()
            .Day(Mon, "09:15", "15:29", 99.00m, [M("09:20", 99.00m, 100.00m, 99.00m, 99.00m), M("09:25", 99.00m, 99.00m, 98.00m, 99.00m), M("10:30", 99.50m, 101.50m, 99.50m, 101.50m)])
            .Day(Tue, "09:15", "15:29", listed: [M("09:15", 81.30m, 81.50m, 81.00m, 81.20m)]);
        yield return Row("Q2", "P3-03d", t, Cand(t, OracleSide.Buy, Mon, "11:15", 101.50m, 98.00m), Swing(3), new Expect { Entry = 101.50m, EntryF = 101.55m, Exit = 81.30m, ExitF = 81.10m, Reason = OracleExitReason.Stop, R = -5.7714m, RF = -5.8429m, SlipR = -0.0714m, Mae = 5.7714m, Mfe = 0.0m, Held = 2, Extra = { ["StopGapExits"] = 1, ["OvernightStopGaps"] = 1 } });

        // Rows O1 to O9 (P3-03e): Monday's daily bar closes at 15:30, trigger close 100.00, stop 95.00, N = 2.
        Tape OMon() => new Tape().Day(Mon, "09:15", "15:29", 100.00m);
        OracleCandidate OCand(Tape tp, string date = Mon) => Cand(tp, OracleSide.Buy, date, "15:30", 100.00m, 95.00m);

        t = OMon().Day(Tue, "09:15", "15:29", listed: [M("09:15", 100.05m, 100.40m, 99.90m, 100.30m)]).Day(Wed, "09:15", "15:29", listed: [M("15:29", 100.55m, 100.70m, 100.50m, 100.60m)]);
        yield return Row("O1", "P3-03e", t, OCand(t), Swing(2), new Expect { Entry = 100.05m, EntryF = 100.10m, Target = 110.15m, Exit = 100.60m, ExitF = 100.50m, Reason = OracleExitReason.MaxHold, R = 0.1089m, RF = 0.0792m, SlipR = -0.0297m, Mae = 0.0297m, Mfe = 0.1287m, Held = 2, Bars = 750, TradeDate = Tue, EntryAt = Tue + " 09:15", Extra = { ["OvernightEntries"] = 1 } });

        t = OMon().Day(Tue, "09:15", "15:29", listed: [M("09:15", 100.30m, 100.60m, 100.20m, 100.50m)]);
        yield return Row("O2", "P3-03e", t, OCand(t), Swing(2), new Expect { Trade = false, NoTrade = OracleNoTradeReason.LimitNotReached });

        t = OMon().Day(Tue, "09:15", "15:29", listed: [M("09:15", 100.30m, 100.60m, 100.10m, 100.50m)]);
        yield return Row("O3", "P3-03e", t, OCand(t), Swing(2), new Expect { Entry = 100.10m, EntryF = 100.10m, Fill = OracleEntryFill.AtLimit, Exit = 100.50m, Reason = OracleExitReason.EndOfData, R = 0.0784m, Mae = 0.0m, Mfe = 0.0784m, Held = 1, Bars = 375, TradeDate = Tue, Extra = { ["OvernightEntries"] = 1 } });

        t = OMon().Day(Tue, "09:15", "15:29", listed: [M("09:15", 94.50m, 94.80m, 94.20m, 94.60m)]);
        yield return Row("O4", "P3-03e", t, OCand(t), Swing(2), new Expect { Trade = false, NoTrade = OracleNoTradeReason.EntryThroughStop });

        t = OMon().Day(Wed, "09:15", "15:29", listed: [M("09:15", 100.05m, 100.40m, 99.90m, 100.30m)]);
        yield return Row("O5", "P3-03e", t, OCand(t), Swing(2), new Expect { Trade = false, NoTrade = OracleNoTradeReason.NoFillBar });

        t = OMon().Day(Wed, "09:15", "15:29", listed: [M("09:15", 100.05m, 100.40m, 99.90m, 100.30m)]);
        yield return Row("O5b", "P3-03e", t, OCand(t), Swing(2, Cal([Tue])), new Expect { Entry = 100.05m, EntryF = 100.10m, TradeDate = Wed, Exit = 100.30m, Reason = OracleExitReason.EndOfData, Held = 1, Extra = { ["OvernightEntries"] = 1 }, CheckCounters = false });

        t = OMon().Day(Tue, "09:20", "15:29", listed: [M("09:20", 100.05m, 100.40m, 99.90m, 100.30m)]);
        yield return Row("O6-0920", "P3-03e", t, OCand(t), Swing(2), new Expect { Entry = 100.05m, EntryAt = Tue + " 09:20", TradeDate = Tue, Exit = 100.30m, Reason = OracleExitReason.EndOfData, Held = 1, Extra = { ["OvernightEntries"] = 1, ["EntriesAfterMissingMinutes"] = 1 }, CheckCounters = false });

        t = OMon().Day(Tue, "09:21", "15:29", listed: [M("09:21", 100.05m, 100.40m, 99.90m, 100.30m)]);
        yield return Row("O6-0921", "P3-03e", t, OCand(t), Swing(2), new Expect { Trade = false, NoTrade = OracleNoTradeReason.NoFillBar });

        t = new Tape().Day(Fri, "09:15", "15:29", 100.00m).Day(Mon2, "09:15", "15:29", listed: [M("09:15", 100.05m, 100.40m, 99.90m, 100.30m)]);
        yield return Row("O7", "P3-03e", t, OCand(t, Fri), Swing(2), new Expect { Entry = 100.05m, EntryF = 100.10m, TradeDate = Mon2, Exit = 100.30m, Reason = OracleExitReason.EndOfData, Held = 1, Extra = { ["OvernightEntries"] = 1 }, CheckCounters = false });

        t = OMon();
        yield return Row("O8", "P3-03e", t, OCand(t), Swing(2), new Expect { Trade = false, NoTrade = OracleNoTradeReason.OrderPendingAtEnd });
    }
}
