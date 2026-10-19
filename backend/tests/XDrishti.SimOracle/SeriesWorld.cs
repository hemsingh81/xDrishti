namespace XDrishti.SimOracle;

/// <summary>A day-by-day grid of minute slots with a calm random path that stays strictly between the stop and the target.</summary>
internal sealed class SeriesWorld(SplitMix64 rng, decimal tick, int dir, decimal entry, int riskTicks)
{
    private decimal _entry = entry;
    private int _rt = riskTicks;

    internal List<SeriesCell> Cells { get; } = [];

    /// <summary>The current position of the path in favourable ticks from the anchor.</summary>
    internal int Pos { get; set; }

    internal decimal Px(int u) => _entry + (dir * u * tick);

    internal void Reanchor(decimal newEntry, int newRiskTicks)
    {
        _entry = newEntry;
        _rt = newRiskTicks;
        Pos = 0;
    }

    internal int ToU(decimal price) => (int)Math.Round((price - _entry) / (dir * tick), MidpointRounding.AwayFromZero);

    internal void AddSession(DateOnly date, OracleSession s)
    {
        for (int m = s.FromMinute; m < s.ToMinute; m++)
        {
            Cells.Add(new SeriesCell { Epoch = OracleClock.Epoch(date, m), Date = date, Minute = m, Volume = 500, Open = _entry, High = _entry, Low = _entry, Close = _entry });
        }
    }

    internal int IndexOf(DateOnly date, int minute)
    {
        for (int i = 0; i < Cells.Count; i++)
        {
            if (Cells[i].Date == date && Cells[i].Minute == minute)
            {
                return i;
            }
        }

        return -1;
    }

    internal int BarIndexOf(int cell)
    {
        int n = 0;
        for (int i = 0; i < cell; i++)
        {
            if (Cells[i].Present)
            {
                n++;
            }
        }

        return n;
    }

    private int Clamp(int u) => Math.Max(-(_rt - 1), Math.Min((2 * _rt) - 1, u));

    internal void Calm(int from, int to, bool endAtZero)
    {
        for (int i = from; i < to; i++)
        {
            int open = Pos;
            int close = Clamp(open + rng.Range(-2, 2));
            if (endAtZero && i == to - 1)
            {
                close = 0;
            }

            int fav = Clamp(Math.Max(open, close) + rng.Range(0, 1));
            int adv = Clamp(Math.Min(open, close) - rng.Range(0, 1));
            Set(i, Px(open), Px(close), Px(fav), Px(adv), rng.Range(100, 4000));
            Pos = close;
        }
    }

    /// <summary>The fill minute of an AtOpen entry at the anchor: the open is the anchor, the extremes stay inside the bounds.</summary>
    internal void PlantFill(int idx)
    {
        int fav = Math.Min(rng.Range(0, 2), (2 * _rt) - 1);
        int adv = -Math.Min(rng.Range(0, 2), _rt - 1);
        int close = rng.Range(adv, fav);
        PlantU(idx, 0, close, fav, adv);
    }

    internal void PlantU(int idx, int openU, int closeU, int favU, int advU)
    {
        Set(idx, Px(openU), Px(closeU), Px(favU), Px(advU), rng.Range(100, 4000));
        Pos = closeU;
    }

    internal void PlantPx(int idx, decimal open, decimal close, decimal extreme1, decimal extreme2)
    {
        Set(idx, open, close, extreme1, extreme2, rng.Range(100, 4000));
        Pos = ToU(close);
    }

    private void Set(int idx, decimal open, decimal close, decimal e1, decimal e2, long volume)
    {
        SeriesCell c = Cells[idx];
        c.Present = true;
        c.Volume = volume;
        c.Open = open;
        c.Close = close;
        c.High = Math.Max(Math.Max(open, close), Math.Max(e1, e2));
        c.Low = Math.Min(Math.Min(open, close), Math.Min(e1, e2));
    }

    internal void Flat(int from, int to, decimal price)
    {
        for (int i = from; i < to; i++)
        {
            Set(i, price, price, price, price, 300);
        }
    }

    internal void Remove(int idx) => Cells[idx].Present = false;

    /// <summary>A zero-volume minute: flat at the close of the nearest earlier present minute.</summary>
    internal void Zero(int idx)
    {
        decimal price = Px(Pos);
        for (int j = idx - 1; j >= 0; j--)
        {
            if (Cells[j].Present)
            {
                price = Cells[j].Close;
                break;
            }
        }

        Set(idx, price, price, price, price, 0);
    }

    internal void Truncate(int idx) => Cells.RemoveRange(idx, Cells.Count - idx);

    internal int Lo => -(_rt - 1);

    internal int Hi => (2 * _rt) - 1;

    internal List<OracleBar> ToBars()
    {
        var bars = new List<OracleBar>();
        foreach (SeriesCell c in Cells)
        {
            if (c.Present)
            {
                bars.Add(new OracleBar(c.Epoch, (double)c.Open, (double)c.High, (double)c.Low, (double)c.Close, c.Volume));
            }
        }

        return bars;
    }
}
