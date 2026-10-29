using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using XDrishti.Infrastructure.Ai.ReadOnly;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Execution;

/// <summary>
/// Gives the execution role a login (spec P12-01b). The migration creates <c>xd_exec</c> NOLOGIN; <c>xd-api migrate</c> sets <c>LOGIN PASSWORD</c> from the secret file (<c>ExecRole__Password</c>, mounted only into xd-migrate and xd-exec) after the migrations, as the database owner.
/// An absent or empty file sets the role NOLOGIN (fail closed); a missing role is left alone: the stack never starts the execution service by itself. The password is a parameter, never in SQL text, a log line or an error.
/// </summary>
public static partial class ExecRoleProvisioner
{
    public const string Role = "xd_exec";

    public const string SectionName = "ExecRole";

    /// <summary>Provisions the login; returns true when the role now has one.</summary>
    public static async Task<bool> ProvisionAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ExecRoleProvisioner));
        var password = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetSection(SectionName)["Password"];
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using (var exists = new NpgsqlCommand("SELECT EXISTS (SELECT FROM pg_roles WHERE rolname = @role)", connection))
            {
                exists.Parameters.AddWithValue("role", Role);
                if (!(bool)(await exists.ExecuteScalarAsync(cancellationToken))!)
                {
                    LogLeftNoLogin(logger, "the role does not exist (the migrating role could not create it)");
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                // Fail closed: no password file means no login, even if an earlier run gave the role one.
                await using var close = new NpgsqlCommand($"ALTER ROLE {Role} NOLOGIN", connection);
                await close.ExecuteNonQueryAsync(cancellationToken);
                LogLeftNoLogin(logger, "the password file is absent or empty; the role is set NOLOGIN");
                return false;
            }

            await AiRoleProvisioner.SetLoginAsync(connection, Role, password, cancellationToken);
            LogProvisioned(logger);
            return true;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    [LoggerMessage(EventId = 6610, Level = LogLevel.Information, Message = "The execution role keeps no login: {Reason}")]
    private static partial void LogLeftNoLogin(ILogger logger, string reason);

    [LoggerMessage(EventId = 6611, Level = LogLevel.Information, Message = "The execution role has a login")]
    private static partial void LogProvisioned(ILogger logger);
}
