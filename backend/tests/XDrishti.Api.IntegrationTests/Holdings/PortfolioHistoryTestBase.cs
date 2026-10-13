using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>
/// Shared set-up of the portfolio history tests (P2-07): shares and an index in the master, day bars written straight into the <c>1D</c> aggregate (as the aggregate build would leave them), and the calls of the
/// history API. Clock: Monday 2026-10-12 10:00 India time, inside the session. The days are October 2026: Mon 5 ... Fri 9, Mon 12 (today).
/// </summary>
public abstract class PortfolioHistoryTestBase(ApiFactory factory) : HoldingsTestBase(factory)
{
    protected static readonly DateOnly Today = new(2026, 10, 12);

    protected static DateOnly Oct(int day) => new(2026, 10, day);

    protected async Task<Guid> ShareAsync(string symbol, string securityId, string isin) =>
        await Factory.SeedInstrumentAsync(symbol, isin, securityId, symbol + " Limited", Ct);

    /// <summary>An index of the master list (made up); with bars it also gets a coverage row, like an instrument whose bars were fetched.</summary>
    protected async Task<Guid> IndexAsync(string canonical, string displayName, params (DateOnly Date, double Close)[] closes)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at)
            VALUES (@id, @symbol, 'NSE', 'IDX_I', 'index', @name, @name, 1, @symbol, false, now(), now())
            """,
            ("id", id), ("symbol", canonical), ("name", displayName));
        if (closes.Length > 0)
        {
            await StoreDailyAsync(id, closes);
            await ExecuteAsync(
                "INSERT INTO market.bar_coverage (instrument_id, first_bar, last_bar, bars, csv_bars, dhan_bars, updated_at) VALUES (@id, @first, @last, 375, 0, 375, now())",
                ("id", id), ("first", At(closes.Min(c => c.Date))), ("last", At(closes.Max(c => c.Date))));
        }

        return id;
    }

    /// <summary>Writes day bars (open = high = low = close) into the <c>1D</c> aggregate.</summary>
    protected async Task StoreDailyAsync(Guid instrumentId, params (DateOnly Date, double Close)[] closes)
    {
        await using var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        foreach (var (date, close) in closes)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete) VALUES (@id, '1D', @ts, @c, @c, @c, @c, 1000, 375, true) ON CONFLICT DO NOTHING",
                connection);
            command.Parameters.AddWithValue("id", instrumentId);
            command.Parameters.AddWithValue("ts", At(date));
            command.Parameters.AddWithValue("c", close);
            await command.ExecuteNonQueryAsync(Ct);
        }

        InvalidateHistory(); // new bars are announced by the 60-second limit in production; a test does not wait for it
    }

    /// <summary>Drops the cached histories after the test changed the database behind the API's back.</summary>
    protected void InvalidateHistory() => Factory.Services.GetRequiredService<IPortfolioHistoryCache>().Invalidate();

    protected static DateTimeOffset At(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330)).ToUniversalTime();

    protected async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    protected async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
    }

    protected async Task<PortfolioHistory> HistoryAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/portfolio/history" + query, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync<PortfolioHistory>(Ct);
    }

    protected async Task<BenchmarkView> BenchmarkAsync()
    {
        using var response = await Client.GetAsync("/api/portfolio/benchmark", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.ReadAsync<BenchmarkView>(Ct);
    }

    protected async Task<PortfolioTrackResult> TrackAsync(string path)
    {
        using var response = await Client.PostAsync(path, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync<PortfolioTrackResult>(Ct);
    }

    /// <summary>
    /// The number of statements that read the stored day bars while <paramref name="request"/> runs, counted from the driver's own trace events of every command (not a count of calls into our code).
    /// </summary>
    protected static async Task<int> BarReadsDuringAsync(Func<Task> request)
    {
        var reads = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                // our own statement: the portfolio history reads the day aggregate of many instruments at once ("= ANY(@ids)"); other readers of the table (jobs, quality) use other statements
                if (activity.Tags.Any(t => t.Value is { } text && text.Contains("market.bars_agg", StringComparison.Ordinal) && text.Contains("= ANY(@ids)", StringComparison.Ordinal)))
                {
                    Interlocked.Increment(ref reads);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        await request();
        return reads;
    }

    /// <summary>Lot ids of a symbol's holding (the first holding of that symbol).</summary>
    protected async Task<(Guid HoldingId, Guid LotId)> FirstLotAsync(string symbol)
    {
        var page = await Client.ListHoldingsAsync($"?q={symbol}", Ct);
        var holding = page.Items.Single(h => h.Symbol == symbol);
        var lots = await Client.LotsAsync(holding.Id, Ct);
        return (holding.Id, lots.Lots[0].Id);
    }

    protected async Task SetLotDateAsync(string symbol, string date)
    {
        var (holding, lot) = await FirstLotAsync(symbol);
        using var response = await Client.PutJsonAsync($"/api/holdings/{holding}/lots/{lot}", new { acquiredOn = date }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    protected async Task AddSplitAsync(Guid instrumentId, string exDate, int numerator, int denominator)
    {
        using var response = await Client.PostJsonAsync("/api/data/corporate-actions?acknowledgeCsvAdjusted=true", new { instrumentId, exDate, kind = "split", numerator, denominator }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>
    /// One account with ALPHA (100 shares at 50, last price 130) and BETA (10 at 200, last price 520), both mapped to the master and dated Mon 5 Oct, BETA's lot is edited by the tests. ALPHA's stored closes are raw: 200, 220 before a
    /// 1:2 split with ex-date Wed 7 Oct (which the tests add), then 120, 90, 100; BETA's: 500, 510, 520, 480, 500.
    /// </summary>
    protected async Task<(AccountDetail Account, Guid Alpha, Guid Beta)> TwoHoldingsAsync(bool bseOnly = false)
    {
        var alpha = await ShareAsync("ALPHA", "1001", "INE000A01011");
        var beta = await ShareAsync("BETA", "1002", "INE000B01012");
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.AddRange([Holding("ALPHA", 100, 50m, 130m, "1001", "INE000A01011"), Holding("BETA", 10, 200m, 520m, "1002", "INE000B01012")]);
        if (bseOnly)
        {
            Dhan.Holdings.Add(Holding("BSEONLY", 7, 10m, 11m, "5555", null, "BSE"));
        }

        await ImportAllAsync(account.Id, "2026-10-05");
        await StoreDailyAsync(alpha, (Oct(5), 200), (Oct(6), 220), (Oct(7), 120), (Oct(8), 90), (Oct(9), 100));
        await StoreDailyAsync(beta, (Oct(5), 500), (Oct(6), 510), (Oct(7), 520), (Oct(8), 480), (Oct(9), 500));
        return (account, alpha, beta);
    }
}
