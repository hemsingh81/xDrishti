using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Manifest;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>What the result-store tests share: the record text and digests of a run (made as the verb makes them), the store from a host's container, and plain counts read as the owner.</summary>
internal static class StoreKit
{
    public static readonly DateTimeOffset At = new(2026, 10, 19, 9, 30, 0, TimeSpan.Zero);

    public static string TradesCsvText(SimulationRun run, bool openAtEnd)
    {
        var sink = new ArrayBufferWriter<byte>();
        TradesCsv.Write(sink, run.RunKey, openAtEnd ? run.OpenAtEnd : run.Trades);
        return new UTF8Encoding(false, true).GetString(sink.WrittenSpan);
    }

    public static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>The record text and the two digests as the verb makes them from the in-memory run.</summary>
    public static RunRecordText Record(SimulationRun run)
    {
        var trades = TradesCsvText(run, false);
        var open = TradesCsvText(run, true);
        return SimulateStore.Record(run, [new ManifestEntry("trades.csv", trades.Length, Sha256(trades)), new ManifestEntry("open_at_end.csv", open.Length, Sha256(open))]);
    }

    public static RunToStore ToStore(SimulationRun run, DateTimeOffset? at = null, string actor = SimulateStore.Actor) => new(run, Record(run), actor, at ?? At);

    public static async Task<T> WithStoreAsync<T>(IServiceProvider services, Func<IRunStore, Task<T>> action)
    {
        using var scope = services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IRunStore>());
    }

    public static async Task<long> CountAsync(string connectionString, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The rows of the two tables for a run key, as one line (counts and the stored figures), so that a test can compare before and after.</summary>
    public static async Task<string> FingerprintAsync(string connectionString, string runKey, CancellationToken cancellationToken)
    {
        var runs = await CountAsync(connectionString, "SELECT count(*) FROM research.sim_runs WHERE run_key = @k", cancellationToken, ("k", runKey));
        var trades = await CountAsync(connectionString, "SELECT count(*) FROM research.sim_trades t JOIN research.sim_runs r ON r.id = t.run_id WHERE r.run_key = @k", cancellationToken, ("k", runKey));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT coalesce(string_agg(record_sha256 || trades_sha256 || created_by || created_at::text || engine_version, ';'), '') FROM research.sim_runs WHERE run_key = @k", connection);
        command.Parameters.AddWithValue("k", runKey);
        return $"{runs}|{trades}|{await command.ExecuteScalarAsync(cancellationToken)}";
    }
}
