using System.Net;
using System.Net.Http.Json;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// Starts every data test from an empty master, no bars, no jobs and an empty CSV folder, with the clock pinned to Monday 2026-10-12 12:00 India time (the last
/// session that has closed is Friday 2026-10-09) and the owner signed in after the pin.
/// </summary>
public abstract class DataTestBase(ApiFactory factory) : IAsyncLifetime
{
    /// <summary>Monday 2026-10-12 12:00 in India.</summary>
    protected static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    /// <summary>A normal weekday with a closed session before <see cref="Now"/>.</summary>
    protected static readonly DateOnly LastSession = new(2026, 10, 9);

    private static long _securityIds = 1000;

    protected ApiFactory Factory { get; } = factory;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected SignedInClient Owner { get; private set; } = null!;

    /// <summary>Signs in again, for a test that moved the clock far enough to end the session.</summary>
    protected async Task ResignInAsync() => Owner = await Factory.SignInAsync(Ct);

    protected HttpClient Client => Owner.Client;

    public virtual async ValueTask InitializeAsync()
    {
        Factory.Clock.Pin(Now);
        await Factory.ResetAllAsync();
        await Factory.AssertCleanAsync();
        Owner = await Factory.SignInAsync(Ct);
    }

    public virtual async ValueTask DisposeAsync()
    {
        await Factory.ResetClockAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Adds an instrument to the master list (as the instrument refresh would) and returns its id.</summary>
    protected async Task<Guid> SeedAsync(string ticker, string type = "share", bool retired = false, bool dhanId = true, string? canonical = null)
    {
        var id = Guid.CreateVersion7();
        var symbol = canonical ?? $"NSE:{ticker}";
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection,
            """
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at, retired_at)
            VALUES (@id, @symbol, 'NSE', 'NSE_EQ', @type, @name, @name, 1, @ticker, false, now(), now(), CASE WHEN @retired THEN now() END)
            """,
            ("id", id), ("symbol", symbol), ("type", type), ("name", $"{ticker} Ltd"), ("ticker", ticker), ("retired", retired));
        if (dhanId)
        {
            await ExecuteAsync(connection, "INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment) VALUES (@id, 'dhan', @security, 'NSE_EQ')", ("id", id), ("security", Interlocked.Increment(ref _securityIds).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return id;
    }

    protected async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    protected async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }

    protected static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>Writes a bar straight into the table (as the Dhan fetch would) so a test can arrange precedence and gaps.</summary>
    protected async Task StoreAsync(Guid instrumentId, Bar bar, string source)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) VALUES (@id, to_timestamp(@ts), @o, @h, @l, @c, @v, @source) ON CONFLICT (instrument_id, ts) DO UPDATE SET open = EXCLUDED.open, high = EXCLUDED.high, low = EXCLUDED.low, close = EXCLUDED.close, volume = EXCLUDED.volume, source = EXCLUDED.source",
            ("id", instrumentId), ("ts", (double)bar.EpochSeconds), ("o", bar.Open), ("h", bar.High), ("l", bar.Low), ("c", bar.Close), ("v", bar.Volume), ("source", source));
    }

    protected async Task<List<Bar>> StoredBarsAsync(Guid instrumentId)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("SELECT extract(epoch FROM ts)::bigint, open, high, low, close, volume FROM market.bars_1m WHERE instrument_id = @id ORDER BY ts", connection);
        command.Parameters.AddWithValue("id", instrumentId);
        var bars = new List<Bar>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            bars.Add(new Bar(reader.GetInt64(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetInt64(5)));
        }

        return bars;
    }

    protected async Task<Dictionary<(string Timeframe, long Start), ExpectedBar>> StoredAggregatesAsync(Guid instrumentId)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("SELECT timeframe, extract(epoch FROM ts)::bigint, open, high, low, close, volume, bars, complete FROM market.bars_agg WHERE instrument_id = @id ORDER BY timeframe, ts", connection);
        command.Parameters.AddWithValue("id", instrumentId);
        var rows = new Dictionary<(string, long), ExpectedBar>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            rows[(reader.GetString(0), reader.GetInt64(1))] = new ExpectedBar(reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetInt64(6), reader.GetInt32(7), reader.GetBoolean(8));
        }

        return rows;
    }

    protected async Task<HttpResponseMessage> PostRefreshAsync(string[]? steps = null, bool? forceCsv = null) =>
        await Client.PostJsonAsync("/api/data/refresh", steps is null && forceCsv is null ? new { } : new { steps, forceCsv }, Ct);

    protected async Task<DataJobView> StartAsync(string[]? steps = null, bool? forceCsv = null)
    {
        using var response = await PostRefreshAsync(steps, forceCsv);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var job = (await response.Content.ReadFromJsonAsync<DataJobView>(Ct))!;
        response.Headers.Location!.ToString().ShouldBe($"/api/data/jobs/{job.Id}");
        return job;
    }

    /// <summary>Starts a job, lets the background worker's job run to its end and returns the finished job.</summary>
    protected async Task<DataJobView> RunAsync(string[]? steps = null, bool? forceCsv = null)
    {
        var started = await StartAsync(steps, forceCsv);
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        var job = await GetJobAsync(started.Id);
        job.Status.ShouldNotBe("running");
        return job;
    }

    protected async Task<DataJobView> GetJobAsync(Guid id)
    {
        using var response = await Client.GetAsync($"/api/data/jobs/{id}", Ct);
        return await response.ReadAsync<DataJobView>(Ct);
    }

    protected async Task<DataOverview> OverviewAsync()
    {
        using var response = await Client.GetAsync("/api/data/overview", Ct);
        return await response.ReadAsync<DataOverview>(Ct);
    }

    protected async Task<DataInstrumentPage> InstrumentsAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/data/instruments" + query, Ct);
        return await response.ReadAsync<DataInstrumentPage>(Ct);
    }

    protected string CsvPath(string name) => Path.Combine(Factory.CsvFolder, name);

    protected string WriteCsv(string ticker, IEnumerable<Bar> bars, bool gzip = true, string[]? extraLines = null, DateTimeOffset? modified = null) =>
        BarFixtures.WriteFile(Factory.CsvFolder, ticker, BarFixtures.Csv(bars, extraLines ?? []), gzip, modified);
}
