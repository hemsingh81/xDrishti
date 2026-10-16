using XDrishti.Application.Live.Push;
using static XDrishti.Application.Tests.Portfolio.Live.Golden.GoldenWorld;

namespace XDrishti.Application.Tests.Portfolio.Live.Golden;

/// <summary>A small deterministic generator (xorshift32, integer arithmetic only), so the golden file is the same on every machine and a seed names a case.</summary>
internal sealed class GoldenRandom(uint seed)
{
    private uint _state = seed == 0 ? 1u : seed;

    public int Int(int min, int max)
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return min + (int)(_state % (uint)(max - min + 1));
    }

    public bool Chance(int percent) => Int(0, 99) < percent;

    public T Pick<T>(IReadOnlyList<T> items) => items[Int(0, items.Count - 1)];

    /// <summary>A price of 0.0050 to 800.0000 in ten-thousandths of a rupee (most have sub-paise digits).</summary>
    public decimal Price() => Int(50, 8_000_000) / 10_000m;
}

/// <summary>
/// Random portfolios in states a real history can leave behind: a price from a sync (yesterday), a quote (which writes the previous close with its price), a live tick with the close of the same day, or a live tick with an older close; no price
/// at all; holdings without an instrument; closed and partly closed holdings; a broker quantity that differs from the open lots'. Pushes are ticks during the session, and sometimes a quote as the last word of an instrument (only where every
/// holding of it has a previous close, and never followed by a tick: the browser cannot know the close a quote wrote).
/// </summary>
internal static class GoldenGenerator
{
    private static readonly DateTimeOffset Open = Ist(14, 9, 15);

    private static readonly DateTimeOffset Yesterday = Ist(13, 15, 30);

    private static readonly string[] SectorNames = ["Energy", "Banks", "IT", "Pharma", "Auto", "Metals", Unclassified];

    private sealed record Quote(decimal Price, DateTimeOffset? At, string Source, decimal? Close, DateTimeOffset? CloseAt);

    public static GoldenScenario Scenario(string name, string description, uint seed, int holdingCount, int instrumentCount, int accountCount, int bucketCount)
    {
        var r = new GoldenRandom(seed);
        var quotes = Enumerable.Range(0, instrumentCount).Select(_ => StateOf(r)).ToList();
        var sectors = Enumerable.Range(0, instrumentCount).Select(_ => r.Pick(SectorNames)).ToList();
        var holdings = Enumerable.Range(0, holdingCount).Select(n => HoldingOf(r, n, quotes, sectors, accountCount, bucketCount)).ToList();
        var pushes = PushesOf(r, holdings, instrumentCount);
        return GoldenWorld.Scenario(name, description, holdings, pushes, IncomeOf(r, holdings, accountCount));
    }

    /// <summary>
    /// Dividends (to the paisa) on some holdings, charges of trades (four decimals) on some, a charge of a security no holding has, unallocated dividends and a ledger with costs for some accounts. Dates fall on both sides of the lots' dates, as a
    /// real history can.
    /// </summary>
    private static GoldenIncome IncomeOf(GoldenRandom r, List<GoldenHolding> holdings, int accountCount)
    {
        var dividends = new List<GoldenDividend>();
        var charges = new List<GoldenTradeCharge>();
        foreach (var holding in holdings)
        {
            for (var i = r.Chance(45) ? r.Int(1, 3) : 0; i > 0; i--)
            {
                dividends.Add(new GoldenDividend(holding.AccountId, holding.Id, new DateOnly(2024, 3, 1).AddDays(r.Int(0, 950)), r.Int(1, 5_000_000) / 100m));
            }

            for (var i = r.Chance(50) ? r.Int(1, 3) : 0; i > 0; i--)
            {
                charges.Add(new GoldenTradeCharge(holding.AccountId, holding.Symbol, new DateOnly(2024, 1, 1).AddDays(r.Int(0, 800)), r.Int(1, 400_000) / 10_000m));
            }
        }

        var ledgerCharges = new List<GoldenLedgerCharge>();
        var starts = new List<GoldenLedgerStart>();
        for (var account = 0; account < accountCount; account++)
        {
            var id = Id("aa", account);
            if (r.Chance(30))
            {
                dividends.Add(new GoldenDividend(id, null, new DateOnly(2024, 6, 1).AddDays(r.Int(0, 800)), r.Int(1, 100_000) / 100m));
            }

            if (r.Chance(25))
            {
                charges.Add(new GoldenTradeCharge(id, "GONE" + account, new DateOnly(2024, 6, 1).AddDays(r.Int(0, 800)), r.Int(1, 50_000) / 10_000m));
            }

            if (r.Chance(60))
            {
                starts.Add(new GoldenLedgerStart(id, new DateOnly(2025, 1, 1).AddDays(r.Int(0, 300))));
                for (var i = r.Int(0, 3); i > 0; i--)
                {
                    var amount = r.Int(-2_000, 80_000) / 100m;
                    ledgerCharges.Add(new GoldenLedgerCharge(id, new DateOnly(2025, 6, 1).AddDays(r.Int(0, 400)), amount == 0m ? 1m : amount));
                }
            }
        }

        return new GoldenIncome(dividends, charges, ledgerCharges, starts, []);
    }

    private static Quote StateOf(GoldenRandom r)
    {
        if (r.Chance(15))
        {
            return new Quote(0m, null, "sync", null, null);
        }

        var price = r.Price();
        var close = Math.Max(0.0001m, Math.Round(price * (90_000 + r.Int(0, 20_000)) / 100_000m, 4));
        var at = Open.AddMinutes(r.Int(1, 30)).AddSeconds(r.Int(0, 59));
        return r.Int(0, 3) switch
        {
            0 => new Quote(price, Yesterday.AddMinutes(-r.Int(0, 600)), "sync", r.Chance(50) ? close : null, null),
            1 => new Quote(price, at, "quote", close, at),
            2 => new Quote(price, at, "live", close, at.AddMinutes(-r.Int(0, 20))),
            _ => new Quote(price, at, "live", close, Yesterday),
        };
    }

    private static GoldenHolding HoldingOf(GoldenRandom r, int n, List<Quote> quotes, List<string> sectors, int accountCount, int bucketCount)
    {
        var closed = r.Chance(10);
        var instrument = r.Chance(90) ? r.Int(0, quotes.Count - 1) : -1;
        var state = instrument >= 0 ? quotes[instrument] : StateOf(r);
        var lots = new List<GoldenLot>();
        for (var i = r.Int(1, 3); i > 0; i--)
        {
            lots.Add(LotOf(r, closed: closed));
        }

        if (!closed && r.Chance(30))
        {
            lots.Add(LotOf(r, closed: true));
        }

        var openQty = lots.Where(l => l.ClosedOn is null).Sum(l => l.Qty);
        var qty = closed ? 0 : state.Price > 0m && r.Chance(20) ? openQty + r.Int(1, 5) : openQty;
        return new GoldenHolding(
            Id("11", n),
            Id("aa", r.Int(0, accountCount - 1)),
            Id("bb", r.Int(0, bucketCount - 1)),
            instrument >= 0 ? Id("ee", instrument) : null,
            instrument >= 0 ? sectors[instrument] : Unclassified,
            "S" + n.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
            qty,
            closed ? "closed" : "open",
            state.Price,
            state.At,
            state.Source,
            state.Close,
            state.CloseAt,
            lots);
    }

    private static GoldenLot LotOf(GoldenRandom r, bool closed)
    {
        var acquired = new DateOnly(2024, 1, 1).AddDays(r.Int(0, 600));
        var cost = r.Int(100, 6_000_000) / 10_000m;
        return closed
            ? new GoldenLot(r.Int(1, 200), cost, acquired, acquired.AddDays(r.Int(10, 200)), r.Int(100, 6_000_000) / 10_000m)
            : new GoldenLot(r.Int(1, 200), cost, acquired, null, null);
    }

    private static List<InstrumentPrice> PushesOf(GoldenRandom r, List<GoldenHolding> holdings, int instrumentCount)
    {
        var pushes = new List<InstrumentPrice>();
        for (var index = 0; index < instrumentCount; index++)
        {
            var id = Id("ee", index);
            var held = holdings.Where(h => h.InstrumentId == id && h.Status == "open").ToList();
            if (held.Count == 0 || r.Chance(30))
            {
                continue;
            }

            var known = held.Where(h => h.LastPriceAt is not null).Select(h => h.LastPriceAt!.Value).DefaultIfEmpty(Open).Max();
            var baseline = held.Select(h => h.LastPrice).FirstOrDefault(p => p > 0m, 100m);
            var at = (known > Open ? known : Open).AddMinutes(r.Int(1, 5));
            var count = r.Int(1, 3);
            for (var n = 0; n < count; n++)
            {
                at = at.AddSeconds(r.Int(0, 3) + (r.Chance(20) ? 0 : 1));
                var price = Math.Max(0.0001m, Math.Round(baseline * (95_000 + r.Int(0, 10_000)) / 100_000m, 4));
                var quote = n == count - 1 && held.All(h => h.PreviousClose is not null) && r.Chance(20);
                pushes.Add(new InstrumentPrice(id, price, at, quote ? "quote" : "live"));
            }
        }

        return pushes;
    }
}
