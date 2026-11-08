using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>ADR 0224: the fill-minute read of the Lab's capacity check on a real TimescaleDB: <c>BarQueries.ReadMinuteCloseVolumesAsync</c> is an exact-instant read per instrument.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class MinuteCloseVolumeDatabaseTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_exact_instant_returns_close_and_volume_a_missing_one_is_absent_another_instrument_is_not_returned_and_the_start_round_trips_in_IST()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_minute_cv_probe", Ct);
        await using var source = NpgsqlDataSource.Create(scratch.ConnectionString);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var t0 = new DateTimeOffset(2024, 7, 5, 3, 45, 0, TimeSpan.Zero); // 09:15 IST
        await InsertAsync(source, a, t0, 101.5, 1200);
        await InsertAsync(source, a, t0.AddMinutes(1), 102.0, 800);
        await InsertAsync(source, b, t0.AddMinutes(2), 55.25, 77);
        var queries = new BarQueries(source);

        var rows = await queries.ReadMinuteCloseVolumesAsync(a, [t0, t0.AddMinutes(2), t0.AddMinutes(5)], Ct);

        var hit = rows.ShouldHaveSingleItem("the missing minute and the other instrument's minute are absent");
        hit.Close.ShouldBe(101.5);
        hit.Volume.ShouldBe(1200);
        hit.Start.ToUnixTimeSeconds().ShouldBe(t0.ToUnixTimeSeconds());
        hit.Start.Offset.ShouldBe(TimeSpan.FromHours(5.5));
        (await queries.ReadMinuteCloseVolumesAsync(a, [], Ct)).ShouldBeEmpty();
    }

    private static async Task InsertAsync(NpgsqlDataSource source, Guid instrument, DateTimeOffset ts, double close, long volume)
    {
        await using var command = source.CreateCommand("INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) VALUES (@id, @ts, @c, @c, @c, @c, @v, 'csv')");
        command.Parameters.AddWithValue("id", instrument);
        command.Parameters.AddWithValue("ts", ts.UtcDateTime);
        command.Parameters.AddWithValue("c", close);
        command.Parameters.AddWithValue("v", volume);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
