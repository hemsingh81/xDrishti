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
                [.. root.GetProperty("instruments").EnumerateArray().Select(i => new FixtureInstrument(Str(i, "symbol"), Str(i, "name"), i.TryGetProperty("type", out var type) ? type.GetString() ?? "share" : "share"))],
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
                    [.. learning.GetProperty("hypotheses").EnumerateArray().Select(l => new FixtureStatusItem(Str(l, "key"), Str(l, "status")))]),
                root.TryGetProperty("bars", out var bars)
                    ? new FixtureBars(Date(bars, "start"), bars.GetProperty("sessions").GetInt32(), [.. bars.GetProperty("series").EnumerateArray().Select(x => new FixtureSeries(Str(x, "symbol"), x.GetProperty("from").GetDouble(), x.GetProperty("to").GetDouble()))])
                    : null,
                root.TryGetProperty("evidence", out var evidence) ? Evidence(evidence) : null);
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

        if (f.Instruments.Any(i => i.Type is not ("share" or "index")))
        {
            return "an instrument type is not share or index";
        }

        if (f.Bars is { } bars && (bars.Sessions is < 2 or > 400 || bars.Series.Any(x => !symbols.Contains(x.Symbol) || x.From <= 0 || x.To <= 0) || bars.Series.Select(x => x.Symbol).Distinct().Count() != bars.Series.Count))
        {
            return "the bars are not valid (sessions 2 to 400, a listed symbol once, positive prices)";
        }

        if (f.Evidence is { } ev && (ev.Replay.Equity.Count == 0 || ev.Replay.Trades.Any(t => !symbols.Contains(t.Symbol) || t.Side is not ("BUY" or "SELL")) || ev.Shadow.R.Count is < 10 or > 200))
        {
            return "the evidence is not valid (an equity point per session, listed symbols, BUY or SELL, 10 to 200 shadow R values)";
        }

        var signals = f.Runs.SelectMany(r => r.Trades.Select(t => (r.Strategy, t.Symbol, t.Side, t.Entry))).ToList();
        if (signals.Distinct().Count() != signals.Count)
        {
            return "two trades are the same signal";
        }

        return f.Runs.Any(r => r.Trades.Any(t => t.Side is not ("BUY" or "SELL"))) ? "a trade side is not BUY or SELL" : null;
    }

    private static FixtureEvidence Evidence(JsonElement e)
    {
        var replay = e.GetProperty("replay");
        var shadow = e.GetProperty("shadow");
        return new FixtureEvidence(
            new FixtureReplay(
                Date(replay, "from"), Date(replay, "to"), replay.GetProperty("capital").GetDecimal(), replay.GetProperty("filtersWithoutHistory").GetBoolean(), Strings(replay, "labels"),
                [.. replay.GetProperty("equity").EnumerateArray().Select(x => x.GetDecimal())],
                [.. replay.GetProperty("trades").EnumerateArray().Select(t => new FixtureReplayTrade(Str(t, "symbol"), Str(t, "side"), Date(t, "entry"), Date(t, "exit"), t.GetProperty("qty").GetInt32(), t.GetProperty("entryPrice").GetDecimal(), t.GetProperty("stopPrice").GetDecimal(), t.GetProperty("grossPnl").GetDecimal(), t.GetProperty("r").GetDouble()))]),
            new FixtureShadow(Date(shadow, "session"), [.. shadow.GetProperty("r").EnumerateArray().Select(x => x.GetDouble())]));
    }

    private static FixtureRun Run(JsonElement r) =>
        new(Str(r, "key"), Str(r, "strategy"), Str(r, "timeframe"), Str(r, "style"), Str(r, "exit"), Str(r, "universe"), Date(r, "windowStart"), Date(r, "windowEnd"),
            [.. r.GetProperty("trades").EnumerateArray().Select(t => new FixtureTrade(Str(t, "symbol"), Str(t, "side"), Date(t, "entry"), t.GetProperty("r").GetDouble()))]);

    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString() ?? throw new FormatException(name);

    private static List<string> Strings(JsonElement e, string name) => [.. e.GetProperty(name).EnumerateArray().Select(x => x.GetString() ?? throw new FormatException(name))];

    private static DateOnly Date(JsonElement e, string name) => DateOnly.ParseExact(Str(e, name), "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
