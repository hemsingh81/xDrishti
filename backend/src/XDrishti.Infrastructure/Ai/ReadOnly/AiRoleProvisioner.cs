using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>
/// Gives the two read-only roles a login (spec P9-02 AC 13). The migration creates them NOLOGIN; passwords cannot be read by a migration, so <c>xd-api migrate</c> sets <c>LOGIN PASSWORD</c> from the two secret files after the migrations, as the database owner. Both files present and
/// non-empty: both roles get <c>LOGIN</c> and a <c>scram-sha-256</c> password; otherwise the roles are left as they are (NOLOGIN) and event 6020 says why. Running it again only sets the same passwords. A password is passed as a parameter, never in SQL text, a log line or an error.
/// </summary>
public static class AiRoleProvisioner
{
    private const string Read = "xd_mcp_read";
    private const string Sql = "xd_mcp_sql";

    /// <summary>Provisions the logins; returns true when both roles now have one.</summary>
    public static async Task<bool> ProvisionAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AiRoleProvisioner));
        var section = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetSection(AiRolesOptions.SectionName);
        var options = new AiRolesOptions { ReadPassword = section["ReadPassword"], SqlPassword = section["SqlPassword"] };
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await RoleExistsAsync(connection, Read, cancellationToken) || !await RoleExistsAsync(connection, Sql, cancellationToken))
            {
                logger.RolesLeftNoLogin("the roles do not exist (the migrating role could not create them)");
                return false;
            }

            if (string.IsNullOrWhiteSpace(options.ReadPassword) || string.IsNullOrWhiteSpace(options.SqlPassword))
            {
                logger.RolesLeftNoLogin("a password file is absent or empty");
                return false;
            }

            await SetLoginAsync(connection, Read, options.ReadPassword, cancellationToken);
            await SetLoginAsync(connection, Sql, options.SqlPassword, cancellationToken);
            logger.RolesProvisioned();
            return true;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> RoleExistsAsync(NpgsqlConnection connection, string role, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT FROM pg_roles WHERE rolname = @role)", connection);
        command.Parameters.AddWithValue("role", role);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>The password goes in as a parameter of a session setting and is quoted by <c>format('%L')</c> inside the server; the role name is one of two constants.</summary>
    private static async Task SetLoginAsync(NpgsqlConnection connection, string role, string password, CancellationToken cancellationToken)
    {
        await using (var set = new NpgsqlCommand("SELECT set_config('xd.pw', @password, false)", connection))
        {
            set.Parameters.AddWithValue("password", password);
            await set.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            var sql = $"DO $$ BEGIN PERFORM set_config('password_encryption', 'scram-sha-256', true); EXECUTE format('ALTER ROLE {role} WITH LOGIN PASSWORD %L', current_setting('xd.pw')); END $$";
            await using var alter = new NpgsqlCommand(sql, connection);
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await using var reset = new NpgsqlCommand("SELECT set_config('xd.pw', '', false)", connection);
            await reset.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
