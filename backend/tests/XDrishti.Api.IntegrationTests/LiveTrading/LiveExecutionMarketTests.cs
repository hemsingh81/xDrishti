using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.LiveTrading;

namespace XDrishti.Api.IntegrationTests.LiveTrading;

/// <summary>P11-02 on a real database: the stored market facts of the execution feedback - the tick in rupees from the master's paise, the 20-session turnover in crore before a day, the open of the minute an order was created in, and the extremes of an order's life.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LiveExecutionMarketTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Day = new(2026, 11, 3);

    private async Task<TResult> WithMarket<TResult>(Func<ILiveExecutionMarket, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ILiveExecutionMarket>());
    }

    private static DateTimeOffset At(string time) => new(Day.ToDateTime(TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture)), TimeSpan.FromMinutes(330));

    private async Task MinuteAsync(Guid id, string time, double open, double high, double low, double close)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) VALUES (@id, @ts, @o, @h, @l, @c, 100, 'csv')", ("id", id), ("ts", At(time).ToUniversalTime()), ("o", open), ("h", high), ("l", low), ("c", close));
    }

    private async Task DailyAsync(Guid id, DateOnly day, double close, long volume)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete) VALUES (@id, '1D', @ts, @c, @c, @c, @c, @v, 375, true)",
            ("id", id), ("ts", new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330)).ToUniversalTime()), ("c", close), ("v", volume));
    }

    [Fact]
    public async Task The_tick_is_the_masters_paise_in_rupees_and_a_missing_or_refused_tick_is_null()
    {
        await SeedAsync("RELIANCE");
        await SeedAsync("NOTICK");
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.instruments SET tick_size = 5 WHERE canonical_symbol = 'NSE:RELIANCE'");
        }

        (await WithMarket(m => m.TickAsync("RELIANCE", Ct).AsTask())).ShouldBe(0.05m);
        (await WithMarket(m => m.TickAsync("NOTICK", Ct).AsTask())).ShouldBeNull(); // the fallback tick is not the instrument's own
        (await WithMarket(m => m.TickAsync("UNKNOWN", Ct).AsTask())).ShouldBeNull();
    }

    [Fact]
    public async Task The_turnover_is_the_mean_of_close_times_volume_over_the_20_sessions_before_the_day_in_crore()
    {
        var id = await SeedAsync("RELIANCE");
        for (var i = 1; i <= 20; i++)
        {
            await DailyAsync(id, Day.AddDays(-i), 1000, 6_400_000); // 640 crore a day (1,000 x 6.4 million shares)
        }

        await DailyAsync(id, Day, 1, 1); // the day itself is not counted
        var thin = await SeedAsync("THIN");
        for (var i = 1; i <= 19; i++)
        {
            await DailyAsync(thin, Day.AddDays(-i), 1000, 6_400_000);
        }

        (await WithMarket(m => m.AdvTurnoverCrAsync("RELIANCE", Day, Ct).AsTask())).ShouldBe(640m);
        (await WithMarket(m => m.AdvTurnoverCrAsync("THIN", Day, Ct).AsTask())).ShouldBeNull(); // fewer than 20 sessions
        (await WithMarket(m => m.AdvTurnoverCrAsync("UNKNOWN", Day, Ct).AsTask())).ShouldBeNull();
    }

    [Fact]
    public async Task The_minute_open_and_the_extremes_of_an_orders_life_come_from_the_stored_bars()
    {
        var id = await SeedAsync("RELIANCE");
        await MinuteAsync(id, "09:40", 1250.20, 1251.00, 1249.90, 1250.50);
        await MinuteAsync(id, "09:41", 1250.50, 1252.00, 1250.10, 1251.80);
        await MinuteAsync(id, "09:42", 1251.80, 1253.00, 1250.90, 1252.50);

        (await WithMarket(m => m.MinuteOpenAsync("RELIANCE", At("09:40:37"), Ct).AsTask())).ShouldBe(1250.20m);
        (await WithMarket(m => m.MinuteOpenAsync("RELIANCE", At("09:39:59"), Ct).AsTask())).ShouldBeNull();
        (await WithMarket(m => m.MinuteExtremesAsync("RELIANCE", At("09:40:37"), At("09:42:10"), Ct).AsTask())).ShouldBe((1249.90m, 1253.00m));
        (await WithMarket(m => m.MinuteExtremesAsync("RELIANCE", At("09:41:05"), At("09:41:50"), Ct).AsTask())).ShouldBe((1250.10m, 1252.00m));
        (await WithMarket(m => m.MinuteExtremesAsync("RELIANCE", At("10:00:00"), At("10:05:00"), Ct).AsTask())).ShouldBe((null, null));
    }
}
