using System.Globalization;
using System.Text.Json;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>Reads <c>fixture.json</c> (spec P9-09 AC 1). Pure: text in, the fixture or a typed failure out; a fixture that does not hold together (a trade of an unknown symbol, a basket of an unknown symbol, a duplicate signal) is refused, so a seeder never runs on a broken file.</summary>
public static class EvalFixtureReader
{
    public const string InvalidCode = "eval.invalid_fixture";

    public static Result<EvalFixture> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var plan = root.GetProperty("plan");
            var portfolio = root.GetProperty("portfolio");
            var cell = root.GetProperty("cellStats");
            var learning = root.GetProperty("learning");
            var fixture = new EvalFixture(
                Date(root, "fixtureDate"),
                [.. root.GetProperty("instruments").EnumerateArray().Select(i => new FixtureInstrument(Str(i, "symbol"), Str(i, "name")))],
                [.. root.GetProperty("baskets").EnumerateArray().Select(b => new FixtureBasket(Str(b, "name"), Strings(b, "symbols")))],
                [.. root.GetProperty("runs").EnumerateArray().Select(Run)],
                new FixturePlan(
                    Date(plan, "date"), Str(plan, "paperAccount"),
                    [.. plan.GetProperty("primary").EnumerateArray().Select(t => new FixtureTicket(t.GetProperty("rank").GetInt32(), Str(t, "symbol"), Str(t, "side"), Str(t, "strategy"), Str(t, "timeframe"), t.GetProperty("allocationShares").GetInt32()))],
                    [.. plan.GetProperty("reserve").EnumerateArray().Select(r => Str(r, "symbol"))],
                    [.. plan.GetProperty("rejected").EnumerateArray().Select(r => new FixtureRejected(Str(r, "symbol"), Str(r, "reason")))],
                    Str(plan.GetProperty("primary")[0], "cell"), plan.GetProperty("primary")[0].GetProperty("cellTrades").GetInt32()),
                new FixturePortfolio(
                    Str(portfolio, "account"), Str(portfolio, "basket"),
                    [.. portfolio.GetProperty("holdings").EnumerateArray().Select(h => new FixtureHolding(Str(h, "symbol"), h.GetProperty("shares").GetInt32(), Date(h, "boughtOn"), h.GetProperty("buyPrice").GetDecimal(), h.GetProperty("lastPrice").GetDecimal()))]),
                new FixtureCellStats(Str(cell, "cell"), [.. cell.GetProperty("windows").EnumerateArray().Select(w => new FixtureCellWindow(Str(w, "window"), w.GetProperty("n").GetInt32()))]),
                new FixtureLearning(
                    [.. learning.GetProperty("lessons").EnumerateArray().Select(l => new FixtureStatusItem(Str(l, "key"), Str(l, "status")))],
                    [.. learning.GetProperty("hypotheses").EnumerateArray().Select(l => new FixtureStatusItem(Str(l, "key"), Str(l, "status")))]));
            var problem = Check(fixture);
            return problem is null ? Result.Success(fixture) : Result.Failure<EvalFixture>(new Error(InvalidCode, problem, ErrorKind.Validation));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentOutOfRangeException)
        {
            return Result.Failure<EvalFixture>(new Error(InvalidCode, "fixture.json is not a fixture", ErrorKind.Validation));
        }
    }

    private static string? Check(EvalFixture f)
    {
        var symbols = f.Instruments.Select(i => i.Symbol).ToHashSet(StringComparer.Ordinal);
        if (symbols.Count != f.Instruments.Count)
        {
            return "a symbol is listed twice";
        }

        if (f.Baskets.Any(b => b.Symbols.Any(s => !symbols.Contains(s))) || f.Runs.Any(r => r.Trades.Any(t => !symbols.Contains(t.Symbol))))
        {
            return "a basket or a trade names a symbol that is not an instrument";
        }

        var signals = f.Runs.SelectMany(r => r.Trades.Select(t => (r.Strategy, t.Symbol, t.Side, t.Entry))).ToList();
        if (signals.Distinct().Count() != signals.Count)
        {
            return "two trades are the same signal";
        }

        return f.Runs.Any(r => r.Trades.Any(t => t.Side is not ("BUY" or "SELL"))) ? "a trade side is not BUY or SELL" : null;
    }

    private static FixtureRun Run(JsonElement r) =>
        new(Str(r, "key"), Str(r, "strategy"), Str(r, "timeframe"), Str(r, "style"), Str(r, "exit"), Str(r, "universe"), Date(r, "windowStart"), Date(r, "windowEnd"),
            [.. r.GetProperty("trades").EnumerateArray().Select(t => new FixtureTrade(Str(t, "symbol"), Str(t, "side"), Date(t, "entry"), t.GetProperty("r").GetDouble()))]);

    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString() ?? throw new FormatException(name);

    private static List<string> Strings(JsonElement e, string name) => [.. e.GetProperty(name).EnumerateArray().Select(x => x.GetString() ?? throw new FormatException(name))];

    private static DateOnly Date(JsonElement e, string name) => DateOnly.ParseExact(Str(e, name), "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
