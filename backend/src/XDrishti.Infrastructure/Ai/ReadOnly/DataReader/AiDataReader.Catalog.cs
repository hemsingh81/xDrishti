using System.Text.Json.Nodes;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Infrastructure.Ai.ReadOnly.DataReader;

internal sealed partial class AiDataReader
{
    private const string MembersAsOfNow = "members are as of today, not as of any past run";

    public async Task<AiToolData> GetBasketAsync(string name, CancellationToken cancellationToken)
    {
        var baskets = await QueryAsync("SELECT id, name, purpose FROM aiv.baskets WHERE archived_at IS NULL AND lower(name) = lower(@n) ORDER BY position LIMIT 1", cancellationToken, ("n", name));
        if (baskets.Count == 0)
        {
            return AiToolData.Fail("basket.unknown", "No basket has that name.");
        }

        var members = await QueryAsync(
            """
            SELECT m.symbol, i.name, i.sector, c.first_day, c.last_day, c.bars
            FROM aiv.basket_members m
            LEFT JOIN aiv.instruments i ON i.symbol = m.symbol
            LEFT JOIN LATERAL (SELECT min(b.trade_date) AS first_day, max(b.trade_date) AS last_day, count(*) AS bars FROM aiv.bars b WHERE b.symbol = m.symbol AND b.timeframe = '1D') c ON true
            WHERE m.basket_id = @id AND m.valid_to IS NULL
            ORDER BY m.position
            LIMIT 400
            """,
            cancellationToken,
            ("id", Guid.Parse((string)baskets[0][0]!)));
        var withBars = members.Count(m => m[5] is long b && b > 0);
        return AiToolData.OfRows(
            [new("symbol", false), new("name", true), new("sector", true), new("hasDailyBars", false), new("firstDailyBar", false), new("lastDailyBar", false)],
            [.. members.Select(m => (IReadOnlyList<object?>)[m[0], m[1], m[2], m[5] is long b && b > 0, m[3], m[4]])],
            new ToolEvidence("stored basket membership", ["aiv.baskets", "aiv.basket_members"], null, members.Count, [MembersAsOfNow, "hasDailyBars says whether daily price bars are stored for the symbol"], []),
            [],
            new JsonObject { ["nameText"] = (string?)baskets[0][1], ["purposeText"] = (string?)baskets[0][2], ["memberCount"] = members.Count, ["withDailyBars"] = withBars });
    }

    public async Task<AiToolData> GetInstrumentAsync(string symbol, int bars, CancellationToken cancellationToken)
    {
        var found = await QueryAsync("SELECT symbol, exchange, segment, type, name, isin, series, lot_size, sector, fno_eligible, retired_at FROM aiv.instruments WHERE upper(symbol) = upper(@s) ORDER BY retired_at NULLS FIRST LIMIT 1", cancellationToken, ("s", symbol));
        if (found.Count == 0)
        {
            return AiToolData.Fail("instrument.unknown", "No instrument has that symbol.");
        }

        var canonical = (string)found[0][0]!;
        var coverage = await QueryAsync("SELECT count(*), min(trade_date), max(trade_date) FROM aiv.bars WHERE symbol = @s AND timeframe = '1D'", cancellationToken, ("s", canonical));
        var quality = await QueryAsync("SELECT status, score, missing_minutes, ran_at FROM aiv.quality_results WHERE symbol = @s ORDER BY ran_at DESC LIMIT 1", cancellationToken, ("s", canonical));
        var latest = bars == 0 ? [] : await QueryAsync("SELECT trade_date, open, high, low, close, volume FROM aiv.bars WHERE symbol = @s AND timeframe = '1D' ORDER BY ts DESC LIMIT @n", cancellationToken, ("s", canonical), ("n", bars));
        var count = coverage[0][0] is long c ? c : 0;
        var data = new JsonObject
        {
            ["symbol"] = canonical,
            ["exchange"] = (string?)found[0][1],
            ["segment"] = (string?)found[0][2],
            ["type"] = (string?)found[0][3],
            ["nameText"] = (string?)found[0][4],
            ["isin"] = (string?)found[0][5],
            ["series"] = (string?)found[0][6],
            ["lotSize"] = found[0][7] is int lot ? lot : null,
            ["sectorText"] = (string?)found[0][8],
            ["fnoEligible"] = found[0][9] is bool f ? f : null,
            ["retiredAt"] = (string?)found[0][10],
            ["hasDailyBars"] = count > 0,
            ["dailyBarCount"] = count,
            ["firstDailyBar"] = (string?)coverage[0][1],
            ["lastDailyBar"] = (string?)coverage[0][2],
            ["latestQuality"] = quality.Count == 0 ? "unavailable (no data-quality run for this symbol)" : $"{quality[0][0]}, score {quality[0][1]}, {quality[0][2]} missing minutes, run {quality[0][3]}",
        };
        return AiToolData.OfRows(
            [new("date", false), new("open", false), new("high", false), new("low", false), new("close", false), new("volume", false)],
            [.. latest.Select(r => (IReadOnlyList<object?>)r)],
            new ToolEvidence("stored instrument master and daily bars", ["aiv.instruments", "aiv.bars", "aiv.quality_results"], count > 0 ? $"{coverage[0][1]}..{coverage[0][2]}" : null, latest.Count, ["prices are adjusted at the source", MembersAsOfNow, "missing minutes come from the latest data-quality run (1-minute data)"], []),
            [],
            data);
    }
}
