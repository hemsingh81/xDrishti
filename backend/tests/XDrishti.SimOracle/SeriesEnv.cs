namespace XDrishti.SimOracle;

/// <summary>The random parameters of one generated series.</summary>
internal sealed class SeriesEnv
{
    internal SplitMix64 Rng { get; init; } = new(0);

    internal decimal Tick { get; init; }

    internal int Dir { get; init; }

    /// <summary>The trigger close (a multiple of the tick).</summary>
    internal decimal Entry { get; init; }

    /// <summary>Risk in ticks.</summary>
    internal int Rt { get; init; }

    internal DateOnly Date { get; init; }

    internal int Dm { get; init; }

    internal OracleSlippage Slip { get; init; } = OracleSlippage.None;

    internal OracleSide Side => Dir > 0 ? OracleSide.Buy : OracleSide.Sell;

    internal decimal StopPx => Entry - (Dir * Rt * Tick);

    internal static SeriesEnv Make(SplitMix64 r, bool buyOnly, DateOnly? fixedDate = null)
    {
        decimal[] ticks = [0.01m, 0.05m, 0.10m];
        decimal tick = ticks[r.Range(0, 2)];
        return new SeriesEnv
        {
            Rng = r,
            Tick = tick,
            Dir = !buyOnly && r.Chance(33) ? -1 : 1,
            Entry = tick * r.Range(400, 2900),
            Rt = r.Range(8, 40),
            Date = fixedDate ?? new DateOnly(2026, 3, 2).AddDays(7 * r.Range(0, 39)),
            Dm = r.Range(600, 780),
            Slip = r.Chance(60) ? OracleSlippage.Default : OracleSlippage.None,
        };
    }

    /// <summary>The entry limit of the rule E2 with the 0.1 % default.</summary>
    internal decimal Limit() => Dir > 0
        ? Math.Floor(Entry * 1.001m / Tick) * Tick
        : Math.Ceiling(Entry * 0.999m / Tick) * Tick;
}
