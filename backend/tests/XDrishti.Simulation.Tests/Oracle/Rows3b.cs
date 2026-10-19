using static XDrishti.Simulation.Tests.Oracle.Fx;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>Rows B, H and N of spec P3-03b, and the supplementary rows that O computed by hand for the faults the spec rows cannot show.</summary>
internal static class Rows3b
{
    private static Mn A => M("10:00", 100.00m, 100.30m, 99.80m, 100.20m);

    private static Tape Day(decimal start, string to, IEnumerable<Mn> listed, params string[] absent) =>
        new Tape().Day(Mon, "09:15", to, start, listed, absent);

    private static HandRow Row(string id, Tape t, OracleCandidate c, Expect x, OracleRules? rules = null, string source = "P3-03b") => new()
    {
        Id = id,
        Source = source,
        Bars = t.ToArray(),
        Candidate = c,
        Rules = rules ?? Intraday(),
        Expect = x,
    };

    private static HandRow SetupRow(string id, IEnumerable<Mn> listed, Expect x, decimal stop = 99.00m, string[]? absent = null)
    {
        Tape t = Day(100.00m, "15:29", listed, absent ?? []);
        return Row(id, t, Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, stop), x);
    }

    private static HandRow TicketS(string id, IEnumerable<Mn> listed, Expect x)
    {
        Tape t = Day(200.00m, "15:29", [M("10:00", 199.90m, 200.20m, 199.50m, 199.60m), .. listed]);
        return Row(id, t, Cand(t, OracleSide.Sell, Mon, "10:00", 200.00m, 202.00m), x);
    }

    private static HandRow Boundary(string id, OracleSide side, Mn fill, Expect x)
    {
        Tape t = Day(2950.00m, "15:29", [fill]);
        return Row(id, t, Cand(t, side, Mon, "10:00", 2950.00m, side == OracleSide.Buy ? 2921.50m : 2978.50m), x);
    }

    internal static IEnumerable<HandRow> All()
    {
        // Entry boundaries B1 to B9 (the entry only; counters are not given by the table).
        yield return Boundary("B1", OracleSide.Buy, M("10:00", 2951.00m, 2954.00m, 2950.50m, 2951.00m), new Expect { Entry = 2951.00m, CheckCounters = false });
        yield return Boundary("B2", OracleSide.Buy, M("10:00", 2952.95m, 2953.50m, 2952.40m, 2952.95m), new Expect { Entry = 2952.95m, CheckCounters = false });
        yield return Boundary("B3", OracleSide.Buy, M("10:00", 2953.00m, 2953.60m, 2952.95m, 2953.00m), new Expect { Entry = 2952.95m, Fill = OracleEntryFill.AtLimit, CheckCounters = false });
        yield return Boundary("B4", OracleSide.Buy, M("10:00", 2953.00m, 2953.60m, 2953.00m, 2953.00m), new Expect { Trade = false, NoTrade = OracleNoTradeReason.LimitNotReached });
        yield return Boundary("B5", OracleSide.Buy, M("10:00", 2921.50m, 2922.00m, 2920.00m, 2921.50m), new Expect { Trade = false, NoTrade = OracleNoTradeReason.EntryThroughStop });
        yield return Boundary("B6", OracleSide.Sell, M("10:00", 2949.00m, 2949.40m, 2948.10m, 2949.00m), new Expect { Entry = 2949.00m, CheckCounters = false });
        yield return Boundary("B7", OracleSide.Sell, M("10:00", 2947.05m, 2947.40m, 2946.60m, 2947.05m), new Expect { Entry = 2947.05m, CheckCounters = false });
        yield return Boundary("B8", OracleSide.Sell, M("10:00", 2947.00m, 2947.05m, 2946.60m, 2947.00m), new Expect { Entry = 2947.05m, Fill = OracleEntryFill.AtLimit, CheckCounters = false });
        yield return Boundary("B9", OracleSide.Sell, M("10:00", 2947.00m, 2947.00m, 2946.50m, 2947.00m), new Expect { Trade = false, NoTrade = OracleNoTradeReason.LimitNotReached });

        // Trades H1 to H25.
        yield return SetupRow("H1", [A, M("10:40", 101.50m, 102.10m, 101.40m, 102.00m)],
            new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 102.00m, ExitF = 102.00m, Reason = OracleExitReason.Target, R = 2.0m, RF = 1.9m, SlipR = -0.1m, Mae = 0.2m, Mfe = 2.0m });

        yield return SetupRow("H2", [M("10:00", 100.30m, 100.50m, 100.00m, 100.40m), M("15:15", 101.00m, 101.20m, 100.90m, 101.10m)],
            new Expect { Entry = 100.10m, EntryF = 100.10m, Fill = OracleEntryFill.AtLimit, Exit = 101.00m, ExitF = 100.90m, Reason = OracleExitReason.SquareOff, R = 0.8182m, RF = 0.7273m, SlipR = -0.0909m, Mae = 0.0909m, Mfe = 0.8182m });

        yield return SetupRow("H3", [A, M("10:08", 98.60m, 98.90m, 98.40m, 98.50m)],
            new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 98.60m, ExitF = 98.40m, Reason = OracleExitReason.Stop, R = -1.4m, RF = -1.7m, SlipR = -0.3m, Mae = 1.4m, Mfe = 0.3m, Extra = { ["StopGapExits"] = 1 } });

        yield return SetupRow("H4", [A, M("10:20", 99.60m, 99.70m, 98.90m, 99.00m)],
            new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 99.00m, ExitF = 98.80m, Reason = OracleExitReason.Stop, R = -1.0m, RF = -1.3m, SlipR = -0.3m, Mae = 1.0m, Mfe = 0.3m });

        yield return SetupRow("H5", [A, M("10:30", 103.20m, 103.50m, 103.00m, 103.40m)],
            new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 102.00m, ExitF = 102.00m, Reason = OracleExitReason.Target, R = 2.0m, RF = 1.9m, SlipR = -0.1m, Mae = 0.2m, Mfe = 2.0m, Extra = { ["TargetGapExits"] = 1 } });

        yield return SetupRow("H6", [A, M("10:15", 100.50m, 102.40m, 98.70m, 100.00m)],
            new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 99.00m, ExitF = 98.65m, Reason = OracleExitReason.Stop, R = -1.0m, RF = -1.45m, SlipR = -0.45m, Mae = 1.0m, Mfe = 0.3m, Extra = { ["AmbiguousMinutes"] = 1 } });

        yield return SetupRow("H7", [A, M("10:25", 103.00m, 103.30m, 98.90m, 100.00m)],
            new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 102.00m, ExitF = 102.00m, Reason = OracleExitReason.Target, R = 2.0m, RF = 1.9m, SlipR = -0.1m, Mae = 0.2m, Mfe = 2.0m, Extra = { ["TargetGapExits"] = 1 } });

        yield return SetupRow("H8", [M("10:00", 100.00m, 100.20m, 98.90m, 99.50m)],
            new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 99.00m, ExitF = 98.75m, Reason = OracleExitReason.Stop, R = -1.0m, RF = -1.35m, SlipR = -0.35m, Mae = 1.0m, Mfe = 0.0m });

        yield return SetupRow("H9", [A, M("10:01", 100.10m, 100.20m, 99.90m, 100.00m), M("10:07", 98.20m, 98.50m, 98.00m, 98.30m)],
            new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 98.20m, ExitF = 98.00m, Reason = OracleExitReason.Stop, R = -1.8m, RF = -2.1m, SlipR = -0.3m, Mae = 1.8m, Mfe = 0.3m, Extra = { ["StopGapExits"] = 1 } },
            absent: ["10:02-10:06"]);

        yield return SetupRow("H10", [A, M("15:17", 100.30m, 100.45m, 100.20m, 100.25m)],
            new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 100.30m, ExitF = 100.20m, Reason = OracleExitReason.SquareOff, R = 0.3m, RF = 0.1m, SlipR = -0.2m, Mae = 0.2m, Mfe = 0.3m, Extra = { ["LateSquareOffs"] = 1 } },
            absent: ["15:15-15:16"]);

        yield return SetupRow("H11", [A, M("15:15", 98.90m, 99.00m, 98.80m, 98.85m)],
            new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 98.90m, ExitF = 98.70m, Reason = OracleExitReason.Stop, R = -1.1m, RF = -1.4m, SlipR = -0.3m, Mae = 1.1m, Mfe = 0.3m, Extra = { ["StopGapExits"] = 1 } });

        yield return TicketS("H12", [M("11:00", 203.10m, 203.60m, 202.90m, 203.20m)],
            new Expect { Entry = 199.90m, EntryF = 199.80m, Exit = 203.10m, ExitF = 203.30m, Reason = OracleExitReason.Stop, R = -1.5238m, RF = -1.6667m, SlipR = -0.1429m, Mae = 1.5238m, Mfe = 0.1905m, Extra = { ["StopGapExits"] = 1 } });

        {
            Tape t = Day(200.00m, "15:29", [M("10:00", 199.40m, 199.90m, 199.20m, 199.30m), M("12:00", 196.00m, 196.30m, 195.30m, 195.60m)]);
            yield return Row("H13", t, Cand(t, OracleSide.Sell, Mon, "10:00", 200.00m, 202.00m),
                new Expect { Entry = 199.80m, EntryF = 199.80m, Fill = OracleEntryFill.AtLimit, Exit = 195.40m, ExitF = 195.40m, Reason = OracleExitReason.Target, R = 2.0m, RF = 2.0m, SlipR = 0.0m, Mae = 0.0455m, Mfe = 2.0m });
        }

        {
            Tape t = Day(123.47m, "15:29", [M("10:00", 123.42m, 123.60m, 123.30m, 123.50m), M("15:15", 124.47m, 124.60m, 124.30m, 124.50m)]);
            yield return Row("H14", t, Cand(t, OracleSide.Buy, Mon, "10:00", 123.47m, 122.42m),
                new Expect { Entry = 123.42m, EntryF = 123.50m, Exit = 124.47m, ExitF = 124.40m, Reason = OracleExitReason.SquareOff, R = 1.05m, RF = 0.9m, SlipR = -0.15m, Mae = 0.12m, Mfe = 1.05m });
        }

        yield return SetupRow("H15", [M("10:00", 100.05m, 100.30m, 99.90m, 100.20m), M("10:50", 102.00m, 102.30m, 101.90m, 102.20m)],
            new Expect { Entry = 100.05m, EntryF = 100.10m, Exit = 102.15m, ExitF = 102.15m, Reason = OracleExitReason.Target, R = 2.0m, RF = 1.9524m, SlipR = -0.0476m, Mae = 0.1429m, Mfe = 2.0m });

        {
            Tape t = Day(100.00m, "14:50", [A, M("14:50", 100.50m, 100.60m, 100.40m, 100.55m)]);
            yield return Row("H16", t, Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m),
                new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 100.55m, ExitF = 100.45m, Reason = OracleExitReason.EndOfData, R = 0.55m, RF = 0.35m, SlipR = -0.2m, Mae = 0.2m, Mfe = 0.6m });
        }

        yield return TicketS("H17", [M("10:30", 195.20m, 195.40m, 194.90m, 195.00m)],
            new Expect { Entry = 199.90m, EntryF = 199.80m, Exit = 195.70m, ExitF = 195.70m, Reason = OracleExitReason.Target, R = 2.0m, RF = 1.9524m, SlipR = -0.0476m, Mae = 0.1429m, Mfe = 2.0m, Extra = { ["TargetGapExits"] = 1 } });

        yield return TicketS("H18", [M("10:25", 195.50m, 202.40m, 195.40m, 199.00m)],
            new Expect { Entry = 199.90m, EntryF = 199.80m, Exit = 195.70m, ExitF = 195.70m, Reason = OracleExitReason.Target, R = 2.0m, RF = 1.9524m, SlipR = -0.0476m, Mae = 0.1429m, Mfe = 2.0m, Extra = { ["TargetGapExits"] = 1 } });

        yield return TicketS("H19", [M("15:15", 202.30m, 202.60m, 202.10m, 202.20m)],
            new Expect { Entry = 199.90m, EntryF = 199.80m, Exit = 202.30m, ExitF = 202.50m, Reason = OracleExitReason.Stop, R = -1.1429m, RF = -1.2857m, SlipR = -0.1429m, Mae = 1.1429m, Mfe = 0.1905m, Extra = { ["StopGapExits"] = 1 } });

        yield return TicketS("H20", [M("10:15", 199.50m, 202.40m, 195.00m, 199.00m)],
            new Expect { Entry = 199.90m, EntryF = 199.80m, Exit = 202.00m, ExitF = 202.55m, Reason = OracleExitReason.Stop, R = -1.0m, RF = -1.3095m, SlipR = -0.3095m, Mae = 1.0m, Mfe = 0.1905m, Extra = { ["AmbiguousMinutes"] = 1 } });

        yield return SetupRow("H21", [A, M("10:40", 101.50m, 102.30m, 101.40m, 102.00m)],
            new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 102.30m, ExitF = 102.30m, Reason = OracleExitReason.Target, R = 2.0m, RF = 1.9130m, SlipR = -0.0870m, Mae = 0.1739m, Mfe = 2.0m },
            stop: 98.85m);

        {
            Tape t = Day(200.00m, "15:29", [M("10:00", 199.85m, 200.10m, 199.60m, 199.70m), M("10:30", 198.50m, 198.60m, 197.75m, 198.00m)]);
            yield return Row("H22", t, Cand(t, OracleSide.Sell, Mon, "10:00", 200.00m, 200.90m),
                new Expect { Entry = 199.85m, EntryF = 199.80m, Exit = 197.75m, ExitF = 197.75m, Reason = OracleExitReason.Target, R = 2.0m, RF = 1.9524m, SlipR = -0.0476m, Mae = 0.2381m, Mfe = 2.0m });
        }

        yield return SetupRow("H23", [A, M("10:20", 99.60m, 99.70m, 99.05m, 99.30m)],
            new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 99.05m, ExitF = 98.85m, Reason = OracleExitReason.Stop, R = -1.0m, RF = -1.3158m, SlipR = -0.3158m, Mae = 1.0m, Mfe = 0.3158m },
            stop: 99.05m);

        yield return SetupRow("H24", [V0("10:00", 100.60m, 100.60m, 100.60m, 100.60m), M("10:01", 100.00m, 100.20m, 99.90m, 100.10m), V0("10:20", 100.20m, 100.30m, 98.50m, 100.20m), V0("15:15", 98.90m, 99.00m, 98.80m, 100.20m), M("15:16", 100.30m, 100.45m, 100.20m, 100.25m)],
            new Expect { Entry = 100.00m, EntryF = 100.10m, Exit = 100.30m, ExitF = 100.20m, Reason = OracleExitReason.SquareOff, R = 0.3m, RF = 0.1m, SlipR = -0.2m, Mae = 0.1m, Mfe = 0.3m, Extra = { ["EntriesAfterMissingMinutes"] = 1, ["LateSquareOffs"] = 1, ["ZeroVolumeMinutes"] = 3 } });

        {
            Tape t = Day(20.00m, "15:29", [M("10:00", 20.00m, 20.10m, 19.90m, 20.00m), M("10:20", 19.80m, 19.85m, 19.60m, 19.70m)]);
            yield return Row("H25", t, Cand(t, OracleSide.Buy, Mon, "10:00", 20.00m, 19.70m),
                new Expect { Entry = 20.00m, EntryF = 20.00m, Exit = 19.70m, ExitF = 19.30m, Reason = OracleExitReason.Stop, R = -1.0m, RF = -2.3333m, SlipR = -1.3333m, Mae = 1.0m, Mfe = 0.3333m, Extra = { ["RiskUnderFourTicks"] = 1 } },
                Intraday(tick: 0.10m));
        }

        // No-trade rows N1 to N5.
        yield return SetupRow("N1", [M("10:00", 100.40m, 100.60m, 100.20m, 100.50m), M("10:01", 100.05m, 100.15m, 100.00m, 100.10m)],
            new Expect { Trade = false, NoTrade = OracleNoTradeReason.LimitNotReached });

        yield return SetupRow("N2", [M("10:00", 99.40m, 99.80m, 99.30m, 99.70m)],
            new Expect { Trade = false, NoTrade = OracleNoTradeReason.EntryThroughStop }, stop: 99.50m);

        {
            Tape t = Day(100.00m, "15:29", []);
            yield return Row("N3-list-end", t, Cand(t, OracleSide.Buy, Mon, "15:30", 100.00m, 99.00m), new Expect { Trade = false, NoTrade = OracleNoTradeReason.NoFillBar });
            Tape t2 = Day(100.00m, "15:29", []).Day(Tue, "09:15", "15:29");
            yield return Row("N3-next-date", t2, Cand(t2, OracleSide.Buy, Mon, "15:30", 100.00m, 99.00m), new Expect { Trade = false, NoTrade = OracleNoTradeReason.NoFillBar });
            yield return Row("N4", t, Cand(t, OracleSide.Buy, Mon, "15:15", 100.00m, 99.00m), new Expect { Trade = false, NoTrade = OracleNoTradeReason.AfterSquareOff });
        }

        // N5: AtLimit after three absent minutes. The exit numbers are O's own (a flat 100.30 to the square-off at 15:15).
        yield return SetupRow("N5", [M("10:03", 100.20m, 100.40m, 100.10m, 100.30m)],
            new Expect { Entry = 100.10m, Fill = OracleEntryFill.AtLimit, Exit = 100.30m, Reason = OracleExitReason.SquareOff, R = 0.1818m, Mae = 0.0m, Mfe = 0.1818m, Extra = { ["EntriesAfterMissingMinutes"] = 1 } },
            absent: ["10:00-10:02"]);

        // Supplementary rows (O's own hand calculation, source O-own).
        {
            // The limit 2985 x 1.001 = 2987.985 is 59759.7 ticks: down is 2987.95, nearest is 2988.00.
            Tape t = Day(2985.00m, "15:29", [M("10:00", 2988.00m, 2988.50m, 2987.50m, 2988.00m)]);
            yield return Row("S-L1", t, Cand(t, OracleSide.Buy, Mon, "10:00", 2985.00m, 2955.00m),
                new Expect { Entry = 2987.95m, EntryF = 2987.95m, Fill = OracleEntryFill.AtLimit, CheckCounters = false }, source: "O-own");
        }

        // The AtLimit fill minute (high 100.80) must not enter MFE: after the fill the flat 100.20 gives (100.20 - 100.10) / 1.10 = 0.0909.
        yield return SetupRow("S-X5", [M("10:00", 100.30m, 100.80m, 100.00m, 100.20m), M("10:30", 100.20m, 100.20m, 99.00m, 99.50m)],
            new Expect { Entry = 100.10m, EntryF = 100.10m, Fill = OracleEntryFill.AtLimit, Exit = 99.00m, ExitF = 98.75m, Reason = OracleExitReason.Stop, R = -1.0m, RF = -1.2273m, SlipR = -0.2273m, Mae = 1.0m, Mfe = 0.0909m });
    }
}
