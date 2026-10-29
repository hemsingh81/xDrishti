using Npgsql;
using XDrishti.Execution.Application;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Host;

/// <summary>
/// The container health check: <c>dotnet XDrishti.Execution.dll --healthcheck</c>. Exit 0 when the service's heartbeat row is younger than 45 seconds (it beats every 10), 1 otherwise and when the database cannot be reached or the role is not xd_exec.
/// It builds none of the host: configuration, one connection, one query.
/// </summary>
internal static class ExecHealthProbe
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(45);

    public static int ExitCode(TimeSpan? age) => age is { } a && a >= TimeSpan.Zero && a < MaxAge ? 0 : 1;

    public static async Task<int> RunAsync(Action<IConfigurationBuilder>? configure = null)
    {
        try
        {
            var builder = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables().AddKeyPerFile("/run/secrets", optional: true);
            configure?.Invoke(builder);
            var options = builder.Build().GetSection(ExecDatabaseOptions.Section).Get<ExecDatabaseOptions>();
            if (options is null || options.Validate(new(options)).Any())
            {
                return 1;
            }

            var connection = options.Build();
            connection.Pooling = false;
            connection.Timeout = (int)Timeout.TotalSeconds;
            connection.CommandTimeout = (int)Timeout.TotalSeconds;
            using var wait = new CancellationTokenSource(Timeout);
            await using var database = new NpgsqlConnection(connection.ConnectionString);
            await database.OpenAsync(wait.Token);
            await using var command = new NpgsqlCommand("SELECT extract(epoch FROM (now() - last_seen_at))::float8 FROM platform.service_heartbeats WHERE service_name = @name", database);
            command.Parameters.AddWithValue("name", IExecHeartbeats.ServiceName);
            return ExitCode(await command.ExecuteScalarAsync(wait.Token) is double seconds ? TimeSpan.FromSeconds(seconds) : null);
        }
#pragma warning disable CA1031 // any failure means the service is not healthy; the exit code is the answer
        catch (Exception)
#pragma warning restore CA1031
        {
            return 1;
        }
    }
}
