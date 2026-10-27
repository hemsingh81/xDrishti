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
    FixtureLearning Learning)
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

public sealed record FixtureInstrument(string Symbol, string Name);

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
