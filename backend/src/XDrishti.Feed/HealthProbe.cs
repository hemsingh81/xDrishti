using Npgsql;
using XDrishti.Application.Abstractions.Platform;

namespace XDrishti.Feed;

/// <summary>
/// The container health check: <c>dotnet XDrishti.Feed.dll --healthcheck</c> (chiseled images have no shell or curl). Exit 0 when the feed's last heartbeat in the database is less than 60 seconds old - the feed reports every 15 seconds,
/// in and out of its window - and 1 otherwise, also when the database cannot be reached. Deliberately small: it reads the configuration (appsettings, environment, the secret files) and asks the database one question over one plain
/// connection; it builds none of the host (no logging, EF Core or dependency injection), because it runs every 15 seconds in a container of 256 MB next to the feed.
/// </summary>
internal static class HealthProbe
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>The heartbeat is stale after this long (the feed reports every 15 seconds).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    private const string AgeSql = "SELECT extract(epoch FROM (now() - last_seen_at))::float8 FROM platform.service_heartbeats WHERE service_name = @name";

    /// <summary>Exit 0 for a heartbeat younger than <see cref="MaxAge"/>, 1 for none or an older one.</summary>
    public static int ExitCode(TimeSpan? heartbeatAge) => heartbeatAge is { } age && age >= TimeSpan.Zero - MaxAge && age < MaxAge ? 0 : 1;

    /// <param name="configure">For tests: changes the configuration before it is read.</param>
    public static async Task<int> RunAsync(Action<IConfigurationBuilder>? configure = null)
    {
        try
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .AddKeyPerFile(XDrishti.Hosting.HostDefaults.SecretsDirectory, optional: true);
            configure?.Invoke(builder);
            var configuration = builder.Build();
            var connection = new NpgsqlConnectionStringBuilder(configuration["Database:ConnectionString"])
            {
                Pooling = false,
                Timeout = (int)Timeout.TotalSeconds,
                CommandTimeout = (int)Timeout.TotalSeconds,
            };
            if (configuration["Database:Password"] is { Length: > 0 } password)
            {
                connection.Password = password;
            }

            using var wait = new CancellationTokenSource(Timeout);
            await using var database = new NpgsqlConnection(connection.ConnectionString);
            await database.OpenAsync(wait.Token);
            await using var command = new NpgsqlCommand(AgeSql, database);
            command.Parameters.AddWithValue("name", IFeedHeartbeats.ServiceName);
            var seconds = await command.ExecuteScalarAsync(wait.Token);
            return ExitCode(seconds is double age ? TimeSpan.FromSeconds(age) : null);
        }
#pragma warning disable CA1031 // any failure means the feed is not healthy; the exit code is the answer
        catch (Exception)
#pragma warning restore CA1031
        {
            return 1;
        }
    }
}
