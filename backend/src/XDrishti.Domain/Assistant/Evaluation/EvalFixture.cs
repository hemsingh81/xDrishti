using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// The seeded data of the eval database (spec P9-09 "The fixture"), as read from <c>eval/fixture/fixture.json</c>. Symbols are fake; nothing here is a real holding, account or price. The SHA-256 of the file is the <c>fixture_hash</c> the marker table carries.
/// </summary>
public sealed record EvalFixture(
    DateOnly FixtureDate,
    IReadOnlyList<FixtureInstrument> Instruments,
    IReadOnlyList<FixtureBasket> Baskets,
    IReadOnlyList<FixtureRun> Runs,
    FixturePlan Plan,
    FixturePortfolio Portfolio,
    FixtureCellStats CellStats,
    FixtureLearning Learning,
    FixtureBars? Bars = null,
    FixtureEvidence? Evidence = null)
{
    /// <summary>The fixture hash: lower-case hex SHA-256 of the file's bytes.</summary>
    public static string HashOf(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>The hash of a text as UTF-8 with line breaks as <c>\n</c> (so a checkout with other line endings has the same hash; the file holds no byte-order mark).</summary>
    public static string HashOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return HashOf(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n")));
    }
}

/// <summary><c>Type</c> is <c>share</c> or <c>index</c> (the Nifty 500 of the basket reports, BK-12b).</summary>
public sealed record FixtureInstrument(string Symbol, string Name, string Type = "share");

/// <summary>Stored daily bars (BK-12b): <c>Sessions</c> weekdays from <c>Start</c>, each series a straight line of closes from <c>From</c> to <c>To</c>, so that a basket's hold is hand-worked (a basket of A 100 to 110, B 50 to 45, C 200 to 250 held +8.3333 %).</summary>
public sealed record FixtureBars(DateOnly Start, int Sessions, IReadOnlyList<FixtureSeries> Series)
{
    /// <summary>The session dates: weekdays from <see cref="Start"/> (a Saturday or Sunday start moves to Monday).</summary>
    public IReadOnlyList<DateOnly> Dates()
    {
        var dates = new List<DateOnly>(Sessions);
        for (var d = Start; dates.Count < Sessions; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                dates.Add(d);
            }
        }

        return dates;
    }
}

public sealed record FixtureSeries(string Symbol, double From, double To);

/// <summary>One closed trade of the fixture's replay run (AI-01): gross figures only, so that the run is labelled before costs.</summary>
public sealed record FixtureReplayTrade(string Symbol, string Side, DateOnly Entry, DateOnly Exit, int Qty, decimal EntryPrice, decimal StopPrice, decimal GrossPnl, double R);

/// <summary>The one replay run of the fixture (AI-01): its range, capital, the equity at each session close, the hindsight labels beyond the ones the parameters give (<c>survivorship</c>) and its trades.</summary>
public sealed record FixtureReplay(DateOnly From, DateOnly To, decimal Capital, bool FiltersWithoutHistory, IReadOnlyList<string> Labels, IReadOnlyList<decimal> Equity, IReadOnlyList<FixtureReplayTrade> Trades);

/// <summary>The shadow record of the fixture (AI-01): one session whose candidates were all graded, with the gross R of each.</summary>
public sealed record FixtureShadow(DateOnly Session, IReadOnlyList<double> R);

/// <summary>The stored research results of the fixture that <c>get_evidence</c> reads (AI-01).</summary>
public sealed record FixtureEvidence(FixtureReplay Replay, FixtureShadow Shadow);

public sealed record FixtureBasket(string Name, IReadOnlyList<string> Symbols);

public sealed record FixtureTrade(string Symbol, string Side, DateOnly Entry, double R);

public sealed record FixtureRun(string Key, string Strategy, string Timeframe, string Style, string Exit, string Universe, DateOnly WindowStart, DateOnly WindowEnd, IReadOnlyList<FixtureTrade> Trades);

public sealed record FixtureTicket(int Rank, string Symbol, string Side, string Strategy, string Timeframe, int AllocationShares);

public sealed record FixtureRejected(string Symbol, string Reason);

public sealed record FixturePlan(DateOnly Date, string PaperAccount, IReadOnlyList<FixtureTicket> Primary, IReadOnlyList<string> Reserve, IReadOnlyList<FixtureRejected> Rejected, string Cell, int CellTrades);

public sealed record FixtureHolding(string Symbol, int Shares, DateOnly BoughtOn, decimal BuyPrice, decimal LastPrice);

public sealed record FixturePortfolio(string Account, string Basket, IReadOnlyList<FixtureHolding> Holdings);

public sealed record FixtureCellWindow(string Window, int N);

public sealed record FixtureCellStats(string Cell, IReadOnlyList<FixtureCellWindow> Windows);

public sealed record FixtureStatusItem(string Key, string Status);

public sealed record FixtureLearning(IReadOnlyList<FixtureStatusItem> Lessons, IReadOnlyList<FixtureStatusItem> Hypotheses);
