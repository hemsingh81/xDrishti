namespace XDrishti.SimOracle;

/// <summary>The 16 families of adversarial series. Each family plants its case by construction; the trace of the walker is what is counted.</summary>
internal static class SeriesBuilder
{
    private const int QMinute = 915;

    private sealed record Ctx(SeriesEnv E, SeriesWorld W, int Fill, int Q);

    private sealed record SwingCtx(SeriesEnv E, SeriesWorld W, IReadOnlyList<DateOnly> Dates, OracleCalendar? Cal, int N, int Fill);

    internal static OracleCase Build(int category, long seed)
    {
        var rng = new SplitMix64(unchecked(((ulong)seed * 0x9E3779B97F4A7C15UL) ^ ((ulong)category << 52) ^ 0x5DEECE66DUL));
        rng.Next();
        return category switch
        {
            1 => Cat1(rng, seed),
            2 => Cat2(rng, seed),
            3 => Cat34(rng, seed, 3),
            4 => Cat34(rng, seed, 4),
            5 => Cat56(rng, seed, 5),
            6 => Cat56(rng, seed, 6),
            7 => Cat7(rng, seed),
            8 => Cat8(rng, seed),
            9 => seed % 2 == 0 ? Cat9A(rng, seed) : Cat9B(rng, seed),
            10 => Cat10(rng, seed),
            11 => Cat11(rng, seed),
            12 => Cat12(rng, seed),
            13 => seed % 2 == 0 ? Cat13A(rng, seed) : Cat13B(rng, seed),
            14 => Cat14(rng, seed),
            15 => Cat15(rng, seed),
            16 => seed % 2 == 0 ? Cat16A(rng, seed) : Cat16B(rng, seed),
            _ => throw new ArgumentOutOfRangeException(nameof(category), "the categories are 1 to 16"),
        };
    }

    private static Ctx Intraday(SplitMix64 r)
    {
        SeriesEnv e = SeriesEnv.Make(r, false);
        var w = new SeriesWorld(r, e.Tick, e.Dir, e.Entry, e.Rt);
        w.AddSession(e.Date, OracleSession.Regular);
        int fill = w.IndexOf(e.Date, e.Dm);
        w.Calm(0, fill, true);
        return new Ctx(e, w, fill, w.IndexOf(e.Date, QMinute));
    }

    private static OracleCase FinishIntraday(Ctx c, int category, long seed)
    {
        SeriesEnv e = c.E;
        long dec = OracleClock.Epoch(e.Date, e.Dm);
        var cand = new OracleCandidate(e.Side, c.W.BarIndexOf(c.Fill - 1), dec, (double)e.Entry, (double)e.StopPx);
        var rules = new OracleRules(2.0, QMinute, 0.1, e.Slip, e.Tick);
        return new OracleCase(category, seed, c.W.ToBars(), [cand], rules);
    }

    private static decimal Between(SplitMix64 r, decimal a, decimal b, decimal tick)
    {
        decimal lo = Math.Min(a, b);
        decimal hi = Math.Max(a, b);
        int n = (int)((hi - lo) / tick);
        return lo + (r.Range(0, n) * tick);
    }

    private static int BetweenU(SplitMix64 r, int a, int b) => r.Range(Math.Min(a, b), Math.Max(a, b));

    private static void FlatAfter(SeriesWorld w, int ev) => w.Flat(ev + 1, w.Cells.Count, w.Cells[ev].Close);

    private static OracleCase Cat1(SplitMix64 r, long seed)
    {
        Ctx c = Intraday(r);
        SeriesEnv e = c.E;
        decimal lim = e.Limit();
        int j = r.Range(1, 5);
        decimal open = lim + (e.Dir * j * e.Tick);
        decimal toward = open - (e.Dir * r.Range(0, j - 1) * e.Tick);
        decimal away = open + (e.Dir * r.Range(0, 3) * e.Tick);
        decimal close = Between(r, toward, away, e.Tick);
        c.W.PlantPx(c.Fill, open, close, toward, away);
        FlatAfter(c.W, c.Fill);
        return FinishIntraday(c, 1, seed);
    }

    private static OracleCase Cat2(SplitMix64 r, long seed)
    {
        Ctx c = Intraday(r);
        SeriesEnv e = c.E;
        int n = r.Range(0, 3);
        decimal open = c.W.Px(-e.Rt - n);
        decimal a = c.W.Px(-e.Rt - n - r.Range(0, 2));
        decimal b = c.W.Px(-e.Rt - n + r.Range(0, 2));
        c.W.PlantPx(c.Fill, open, Between(r, a, b, e.Tick), a, b);
        FlatAfter(c.W, c.Fill);
        return FinishIntraday(c, 2, seed);
    }

    private static OracleCase Cat34(SplitMix64 r, long seed, int category)
    {
        Ctx c = Intraday(r);
        SeriesEnv e = c.E;
        c.W.PlantFill(c.Fill);
        int ev = r.Chance(30) ? c.Q : r.Range(c.Fill + 3, c.Q - 2);
        c.W.Calm(c.Fill + 1, ev, false);
        int g = r.Range(1, 5);
        int openU = category == 3 ? -e.Rt - g : (2 * e.Rt) + g;
        int fav = openU + r.Range(0, 3);
        int adv = openU - r.Range(0, 3);
        c.W.PlantU(ev, openU, BetweenU(r, adv, fav), fav, adv);
        FlatAfter(c.W, ev);
        return FinishIntraday(c, category, seed);
    }

    private static OracleCase Cat56(SplitMix64 r, long seed, int category)
    {
        Ctx c = Intraday(r);
        SeriesEnv e = c.E;
        c.W.PlantFill(c.Fill);
        int ev = r.Range(c.Fill + 3, c.Q - 2);
        c.W.Calm(c.Fill + 1, ev, false);
        int openU;
        int fav;
        if (category == 5)
        {
            openU = r.Range(-(e.Rt - 1), (2 * e.Rt) - 1);
            fav = (2 * e.Rt) + r.Range(0, 4);
        }
        else
        {
            openU = (2 * e.Rt) + (r.Chance(20) ? 0 : r.Range(1, 4));
            fav = openU + r.Range(0, 3);
        }

        int adv = -e.Rt - r.Range(0, 4);
        c.W.PlantU(ev, openU, BetweenU(r, adv, fav), fav, adv);
        FlatAfter(c.W, ev);
        return FinishIntraday(c, category, seed);
    }

    private static OracleCase Cat7(SplitMix64 r, long seed)
    {
        Ctx c = Intraday(r);
        int s = r.Range(1, 5);
        for (int i = 0; i < s; i++)
        {
            if (r.Chance(60))
            {
                c.W.Remove(c.Fill + i);
            }
            else
            {
                c.W.Zero(c.Fill + i);
            }
        }

        c.W.Pos = 0;
        c.W.PlantFill(c.Fill + s);
        c.W.Calm(c.Fill + s + 1, c.W.Cells.Count, false);
        return FinishIntraday(c, 7, seed);
    }

    private static OracleCase Cat8(SplitMix64 r, long seed)
    {
        Ctx c = Intraday(r);
        SeriesEnv e = c.E;
        c.W.PlantFill(c.Fill);
        c.W.Calm(c.Fill + 1, c.W.Cells.Count, false);
        int runs = r.Range(1, 3);
        int firstAfter = -1;
        for (int i = 0; i < runs; i++)
        {
            int start = r.Range(c.Fill + 3, c.Q - 15);
            int len = r.Range(1, 12);
            for (int k = start; k < start + len; k++)
            {
                c.W.Remove(k);
            }

            if (firstAfter < 0)
            {
                firstAfter = start + len;
            }
        }

        if (r.Chance(50))
        {
            int openU = -e.Rt - r.Range(1, 3);
            int fav = openU + r.Range(0, 2);
            int adv = openU - r.Range(0, 2);
            c.W.PlantU(firstAfter, openU, BetweenU(r, adv, fav), fav, adv);
            FlatAfter(c.W, firstAfter);
        }

        return FinishIntraday(c, 8, seed);
    }

    private static OracleCase Cat9A(SplitMix64 r, long seed)
    {
        Ctx c = Intraday(r);
        c.W.PlantFill(c.Fill);
        c.W.Calm(c.Fill + 1, c.W.Cells.Count, false);
        int s = r.Range(1, 12);
        for (int i = 0; i < s; i++)
        {
            if (r.Chance(60))
            {
                c.W.Remove(c.Q + i);
            }
            else
            {
                c.W.Zero(c.Q + i);
            }
        }

        return FinishIntraday(c, 9, seed);
    }

    private static SwingCtx Swing(SplitMix64 r, DateOnly? firstDate, int n, int extra, OracleCalendar? cal)
    {
        SeriesEnv e = SeriesEnv.Make(r, true, firstDate);
        OracleCalendar calendar = cal ?? OracleCalendar.Default;
        var dates = new List<DateOnly> { e.Date };
        for (int i = 1; i < n + extra; i++)
        {
            dates.Add(calendar.NextTradingDay(dates[^1]));
        }

        var w = new SeriesWorld(r, e.Tick, e.Dir, e.Entry, e.Rt);
        foreach (DateOnly d in dates)
        {
            w.AddSession(d, calendar.SessionOf(d));
        }

        int fill = w.IndexOf(e.Date, e.Dm);
        w.Calm(0, fill, true);
        w.PlantFill(fill);
        w.Calm(fill + 1, w.Cells.Count, false);
        return new SwingCtx(e, w, dates, cal, n, fill);
    }

    private static OracleRules SwingRules(SwingCtx c) =>
        new(2.0, 0, 0.1, c.E.Slip, c.E.Tick, new OracleHold(c.N, c.Cal));

    private static OracleCandidate SwingCandidate(SwingCtx c, int ownCell, long decision, decimal triggerClose) =>
        new(c.E.Side, c.W.BarIndexOf(ownCell), decision, (double)triggerClose, (double)(triggerClose - (c.E.Dir * c.E.Rt * c.E.Tick)));

    private static OracleCase FinishSwing(SwingCtx c, int category, long seed)
    {
        long dec = OracleClock.Epoch(c.E.Date, c.E.Dm);
        OracleCandidate cand = SwingCandidate(c, c.Fill - 1, dec, c.E.Entry);
        return new OracleCase(category, seed, c.W.ToBars(), [cand], SwingRules(c));
    }

    private static int DueCell(SwingCtx c)
    {
        DateOnly t = c.Dates[c.N - 1];
        OracleCalendar cal = c.Cal ?? OracleCalendar.Default;
        return c.W.IndexOf(t, cal.DueMinute(t));
    }

    private static OracleCase Cat9B(SplitMix64 r, long seed)
    {
        SwingCtx c = Swing(r, null, r.Range(1, 4), 1, null);
        int due = DueCell(c);
        if (r.Chance(60))
        {
            c.W.Remove(due);
        }
        else
        {
            c.W.Zero(due);
        }

        return FinishSwing(c, 9, seed);
    }

    private static OracleCase Cat10(SplitMix64 r, long seed)
    {
        Ctx c = Intraday(r);
        SeriesEnv e = c.E;
        SeriesWorld w = c.W;
        switch (seed % 5)
        {
            case 0:
                {
                    decimal lim = e.Limit();
                    decimal e1 = lim + (e.Dir * r.Range(0, 2) * e.Tick);
                    decimal e2 = lim - (e.Dir * r.Range(0, 2) * e.Tick);
                    decimal close = Between(r, e1, e2, e.Tick);
                    w.PlantPx(c.Fill, lim, close, e1, e2);
                    w.Reanchor(lim, e.Rt + (int)Math.Round(Math.Abs(lim - e.Entry) / e.Tick));
                    w.Pos = w.ToU(close);
                    w.Calm(c.Fill + 1, w.Cells.Count, false);
                    break;
                }

            case 1:
                {
                    decimal lim = e.Limit();
                    decimal open = lim + (e.Dir * r.Range(1, 3) * e.Tick);
                    decimal far = open + (e.Dir * r.Range(0, 2) * e.Tick);
                    decimal close = Between(r, lim, far, e.Tick);
                    w.PlantPx(c.Fill, open, close, lim, far);
                    w.Reanchor(lim, e.Rt + (int)Math.Round(Math.Abs(lim - e.Entry) / e.Tick));
                    w.Pos = w.ToU(close);
                    w.Calm(c.Fill + 1, w.Cells.Count, false);
                    break;
                }

            case 2:
            case 3:
                {
                    w.PlantFill(c.Fill);
                    int ev = r.Range(c.Fill + 3, c.Q - 2);
                    w.Calm(c.Fill + 1, ev, false);
                    int openU;
                    int fav;
                    int adv;
                    if (seed % 5 == 2)
                    {
                        openU = r.Range(-(e.Rt - 1), e.Rt);
                        adv = -e.Rt;
                        fav = openU + r.Range(0, 2);
                    }
                    else
                    {
                        openU = r.Range(-(e.Rt - 1), (2 * e.Rt) - 1);
                        fav = 2 * e.Rt;
                        adv = Math.Max(-(e.Rt - 1), openU - r.Range(0, 3));
                    }

                    w.PlantU(ev, openU, BetweenU(r, adv, fav), fav, adv);
                    FlatAfter(w, ev);
                    break;
                }

            default:
                {
                    decimal open = e.Entry - (e.Dir * e.Tick / 2m);
                    decimal close = open + (e.Dir * r.Range(-1, 1) * e.Tick);
                    decimal e1 = open + (e.Dir * r.Range(0, 2) * e.Tick);
                    decimal e2 = open - (e.Dir * r.Range(0, 2) * e.Tick);
                    w.PlantPx(c.Fill, open, close, e1, e2);
                    w.Reanchor(open, e.Rt - 1);
                    w.Pos = w.ToU(close);
                    w.Calm(c.Fill + 1, w.Cells.Count, false);
                    break;
                }
        }

        return FinishIntraday(c, 10, seed);
    }

    private static OracleCase Cat11(SplitMix64 r, long seed)
    {
        Ctx c = Intraday(r);
        int s0 = r.Range(0, 3);
        for (int i = 0; i < s0; i++)
        {
            c.W.Zero(c.Fill + i);
        }

        c.W.Pos = 0;
        int fill = c.Fill + s0;
        c.W.PlantFill(fill);
        c.W.Calm(fill + 1, c.W.Cells.Count, false);
        int z = r.Range(3, 20);
        for (int i = 0; i < z; i++)
        {
            c.W.Zero(r.Range(fill + 1, c.Q - 1));
        }

        return FinishIntraday(c, 11, seed);
    }

    private static OracleCase Cat12(SplitMix64 r, long seed)
    {
        SeriesEnv probe = SeriesEnv.Make(r, true);
        int n = r.Range(2, 4);
        SwingCtx c;
        if (seed % 2 == 0)
        {
            DateOnly d0 = probe.Date.AddDays(r.Range(0, 2));
            var cal = new OracleCalendar([d0.AddDays(1)], new Dictionary<DateOnly, OracleSession>());
            c = Swing(r, d0, n, 1, cal);
        }
        else
        {
            DateOnly d0 = probe.Date.AddDays(4);
            var cal = new OracleCalendar([], new Dictionary<DateOnly, OracleSession> { [d0.AddDays(1)] = new OracleSession(555, 750) });
            c = Swing(r, d0, n, 1, cal);
        }

        return FinishSwing(c, 12, seed);
    }

    private static OracleCase Cat13A(SplitMix64 r, long seed)
    {
        if (r.Chance(50))
        {
            Ctx c = Intraday(r);
            c.W.PlantFill(c.Fill);
            c.W.Calm(c.Fill + 1, c.W.Cells.Count, false);
            c.W.Truncate(r.Range(c.Fill + 10, c.Q - 10));
            return FinishIntraday(c, 13, seed);
        }

        SwingCtx s = Swing(r, null, r.Range(2, 4), 0, null);
        int si = r.Range(0, s.N - 1);
        int m = si == 0 ? r.Range(s.E.Dm + 10, 880) : r.Range(560, 880);
        s.W.Truncate(s.W.IndexOf(s.Dates[si], m));
        return FinishSwing(s, 13, seed);
    }

    private static OracleCase Cat13B(SplitMix64 r, long seed)
    {
        SeriesEnv e = SeriesEnv.Make(r, true);
        var w = new SeriesWorld(r, e.Tick, e.Dir, e.Entry, e.Rt);
        w.AddSession(e.Date, OracleSession.Regular);
        w.Calm(0, w.Cells.Count, true);
        int own = w.Cells.Count - 1;
        if (r.Chance(40))
        {
            int z = r.Range(1, 3);
            int first = w.Cells.Count;
            w.AddSession(OracleCalendar.Default.NextTradingDay(e.Date), new OracleSession(555, 555 + z));
            for (int i = first; i < w.Cells.Count; i++)
            {
                w.Zero(i);
            }
        }

        long dec = OracleClock.Epoch(e.Date, 930);
        var cand = new OracleCandidate(e.Side, w.BarIndexOf(own), dec, (double)e.Entry, (double)e.StopPx);
        var rules = new OracleRules(2.0, 0, 0.1, e.Slip, e.Tick, new OracleHold(r.Range(1, 4), null));
        return new OracleCase(13, seed, w.ToBars(), [cand], rules);
    }

    private static OracleCase Cat14(SplitMix64 r, long seed)
    {
        int v = (int)(seed % 5);
        SeriesEnv e = SeriesEnv.Make(r, true);
        DateOnly mon = e.Date;
        DateOnly d0;
        OracleCalendar? cal = null;
        switch (v)
        {
            case 0:
                d0 = mon.AddDays(4);
                break;
            case 1:
                d0 = mon.AddDays(2);
                cal = new OracleCalendar([mon.AddDays(3)], new Dictionary<DateOnly, OracleSession>());
                break;
            default:
                d0 = mon.AddDays(r.Range(0, 3));
                break;
        }

        OracleCalendar calendar = cal ?? OracleCalendar.Default;
        int n = r.Range(1, 3);
        var dates = new List<DateOnly> { d0 };
        for (int i = 0; i < n + 1; i++)
        {
            dates.Add(calendar.NextTradingDay(dates[^1]));
        }

        var w = new SeriesWorld(r, e.Tick, e.Dir, e.Entry, e.Rt);
        foreach (DateOnly d in dates)
        {
            w.AddSession(d, calendar.SessionOf(d));
        }

        int own = w.IndexOf(d0, 929);
        w.Calm(0, own + 1, true);
        int fc = w.IndexOf(dates[1], 555);
        int fill = fc;
        if (v == 2)
        {
            int z = r.Range(1, 5);
            for (int i = 0; i < z; i++)
            {
                w.Remove(fc + i);
            }

            fill = fc + z;
        }
        else if (v == 3)
        {
            for (int i = 0; i < 6; i++)
            {
                w.Remove(fc + i);
            }

            fill = fc + 6;
        }

        if (v == 4)
        {
            int w1 = r.Range(0, e.Rt - 1);
            int need = (int)Math.Ceiling(0.012m * e.Entry / e.Tick);
            int w2 = Math.Max(0, need - w1);
            int closeU = Math.Max(w.Lo, Math.Min(w.Hi, r.Range(-w1, w2)));
            w.PlantU(fill, 0, closeU, w2, -w1);
        }
        else
        {
            w.PlantFill(fill);
        }

        w.Calm(fill + 1, w.Cells.Count, false);
        long dec = OracleClock.Epoch(d0, 930);
        var cand = new OracleCandidate(e.Side, w.BarIndexOf(own), dec, (double)e.Entry, (double)e.StopPx);
        var rules = new OracleRules(2.0, 0, 0.1, e.Slip, e.Tick, new OracleHold(n, cal));
        return new OracleCase(14, seed, w.ToBars(), [cand], rules);
    }

    private static OracleCase Cat15(SplitMix64 r, long seed)
    {
        SwingCtx c = Swing(r, null, r.Range(1, 4), 1, null);
        if (seed % 2 == 1)
        {
            int due = DueCell(c);
            if (r.Chance(50))
            {
                c.W.Remove(due);
            }
            else
            {
                c.W.Zero(due);
            }
        }

        return FinishSwing(c, 15, seed);
    }

    private static OracleCase Cat16A(SplitMix64 r, long seed)
    {
        SwingCtx c = Swing(r, null, r.Range(1, 3), 1, null);
        var cands = new List<OracleCandidate>
        {
            SwingCandidate(c, c.Fill - 1, OracleClock.Epoch(c.E.Date, c.E.Dm), c.E.Entry),
        };
        if (c.N >= 2 && r.Chance(60))
        {
            int own = c.W.IndexOf(c.Dates[1], 614);
            cands.Add(SwingCandidate(c, own, OracleClock.Epoch(c.Dates[1], 615), c.W.Cells[own].Close));
        }

        int due = DueCell(c);
        DateOnly t = c.Dates[c.N - 1];
        cands.Add(SwingCandidate(c, due, OracleClock.Epoch(t, 930), c.W.Cells[due].Close));
        return new OracleCase(16, seed, c.W.ToBars(), cands, SwingRules(c));
    }

    private static OracleCase Cat16B(SplitMix64 r, long seed)
    {
        SwingCtx c = Swing(r, null, r.Range(2, 3), 0, null);
        DateOnly t = c.Dates[c.N - 1];
        int from = c.W.IndexOf(t, 840);
        for (int i = from; i < c.W.Cells.Count; i++)
        {
            c.W.Zero(i);
        }

        var cands = new List<OracleCandidate>
        {
            SwingCandidate(c, c.Fill - 1, OracleClock.Epoch(c.E.Date, c.E.Dm), c.E.Entry),
        };
        int m2 = r.Range(850, 929);
        int own2 = c.W.IndexOf(t, m2 - 1);
        cands.Add(SwingCandidate(c, own2, OracleClock.Epoch(t, m2), c.W.Cells[own2].Close));
        if (r.Chance(50))
        {
            int own3 = c.W.IndexOf(t, 929);
            cands.Add(SwingCandidate(c, own3, OracleClock.Epoch(t, 930), c.W.Cells[own3].Close));
        }

        return new OracleCase(16, seed, c.W.ToBars(), cands, SwingRules(c));
    }
}
