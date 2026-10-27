using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Mcp.Tests.Support;

namespace XDrishti.Mcp.Tests;

/// <summary>
/// Spec P9-02 AC 13: <c>xd-api migrate</c> gives the two roles a login only when both password files exist; otherwise they stay NOLOGIN and migrate still succeeds; it can run again; no password is logged. Also the migration's <c>Down</c>. Own server: the roles are cluster-wide
/// and this class changes them.
/// </summary>
public sealed class RoleProvisioningTests(UnprovisionedDatabase db) : IClassFixture<UnprovisionedDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<bool> CanLoginAsync(string role, string password)
    {
        try
        {
            await using var connection = new NpgsqlConnection(db.As(role, password));
            await connection.OpenAsync(Ct);
            return true;
        }
        catch (PostgresException failure) when (failure.SqlState is PostgresErrorCodes.InvalidPassword or PostgresErrorCodes.InvalidAuthorizationSpecification)
        {
            return false;
        }
    }

    private static Task<object?> ScalarAsync(string connection, string sql) => ReadOnlyDatabase.ScalarAsync(connection, sql);

    /// <summary>The state the migration leaves: the roles exist without a login or a password (the tests of this class may run in any order).</summary>
    private Task ResetRolesAsync() => ReadOnlyDatabase.ExecuteAsync(db.Owner, "ALTER ROLE xd_mcp_read NOLOGIN PASSWORD NULL; ALTER ROLE xd_mcp_sql NOLOGIN PASSWORD NULL");

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "  ")]
    [InlineData("only-the-read-password", null)]
    [InlineData(null, "only-the-sql-password")]
    [InlineData("only-the-read-password", "")]
    public async Task Without_both_passwords_the_roles_stay_without_a_login_and_the_event_says_so(string? read, string? sql)
    {
        await ResetRolesAsync();
        db.Logs.Lines.Clear();

        var done = await db.ProvisionAsync(read, sql);

        done.ShouldBeFalse();
        (await ScalarAsync(db.Owner, "SELECT count(*) FROM pg_roles WHERE rolname IN ('xd_mcp_read', 'xd_mcp_sql') AND rolcanlogin")).ShouldBe(0L);
        db.Logs.Lines.ShouldContain(l => l.EventId == 6020 && l.Message.Contains("password file is absent or empty", StringComparison.Ordinal));
        db.Logs.Lines.ShouldNotContain(l => l.EventId == 6021);
        foreach (var password in new[] { read, sql }.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            db.Logs.All().ShouldNotContain(password!);
        }
    }

    [Fact]
    public async Task With_both_passwords_both_roles_log_in_with_a_scram_password_and_running_again_is_harmless()
    {
        const string Read = "read-pw-with-'quote-\"-and-$$-and-\\backslash";
        const string Sql = "sql-pw-0123456789";
        await ResetRolesAsync();
        db.Logs.Lines.Clear();

        (await db.ProvisionAsync(Read, Sql)).ShouldBeTrue();
        (await db.ProvisionAsync(Read, Sql)).ShouldBeTrue();

        (await CanLoginAsync("xd_mcp_read", Read)).ShouldBeTrue();
        (await CanLoginAsync("xd_mcp_sql", Sql)).ShouldBeTrue();
        (await CanLoginAsync("xd_mcp_read", Sql)).ShouldBeFalse();
        (await ScalarAsync(db.Owner, "SELECT count(*) FROM pg_authid WHERE rolname IN ('xd_mcp_read', 'xd_mcp_sql') AND rolpassword LIKE 'SCRAM-SHA-256$%'")).ShouldBe(2L);
        (await ScalarAsync(db.Owner, "SELECT count(*) FROM pg_roles WHERE rolname IN ('xd_mcp_read', 'xd_mcp_sql') AND rolcanlogin AND NOT rolsuper")).ShouldBe(2L);
        db.Logs.Lines.Count(l => l.EventId == 6021).ShouldBe(2);
        db.Logs.All().ShouldNotContain("read-pw");
        db.Logs.All().ShouldNotContain(Sql);

        // The limits of the migration are still there after the login was given.
        (await ScalarAsync(db.As("xd_mcp_sql", Sql), "SHOW default_transaction_read_only")).ShouldBe("on");

        // A new password replaces the old one.
        (await db.ProvisionAsync("new-read-pw", "new-sql-pw")).ShouldBeTrue();
        (await CanLoginAsync("xd_mcp_read", Read)).ShouldBeFalse();
        (await CanLoginAsync("xd_mcp_read", "new-read-pw")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_missing_role_is_reported_and_migrate_goes_on()
    {
        await ResetRolesAsync();
        await ReadOnlyDatabase.ExecuteAsync(db.Owner, "ALTER ROLE xd_mcp_read RENAME TO xd_mcp_read_gone");
        try
        {
            db.Logs.Lines.Clear();

            (await db.ProvisionAsync("a", "b")).ShouldBeFalse();

            db.Logs.Lines.ShouldContain(l => l.EventId == 6020 && l.Message.Contains("do not exist", StringComparison.Ordinal));
        }
        finally
        {
            await ReadOnlyDatabase.ExecuteAsync(db.Owner, "ALTER ROLE xd_mcp_read_gone RENAME TO xd_mcp_read");
        }
    }

    [Fact]
    public async Task Down_removes_the_views_the_function_the_table_the_schemas_and_the_roles_and_up_brings_them_back()
    {
        var provider = MigrationHost.Build(db.Owner, null, null, db.Logs);
        await using (provider)
        {
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
            var migrations = context.Database.GetMigrations().ToList();
            var index = migrations.FindIndex(m => m.EndsWith("_AddAiReadAccess", StringComparison.Ordinal));
            index.ShouldBeGreaterThan(0);
            var migrator = context.GetService<IMigrator>();

            await migrator.MigrateAsync(migrations[index - 1], Ct);

            (await ScalarAsync(db.Owner, "SELECT count(*) FROM pg_namespace WHERE nspname IN ('ai', 'aiv')")).ShouldBe(0L);
            (await ScalarAsync(db.Owner, "SELECT count(*) FROM pg_roles WHERE rolname IN ('xd_mcp_read', 'xd_mcp_sql')")).ShouldBe(0L);
            (await ScalarAsync(db.Owner, "SELECT count(*) FROM pg_proc WHERE proname IN ('context_allows', 'stamp_settings')")).ShouldBe(0L);

            await migrator.MigrateAsync(null, Ct);

            (await ScalarAsync(db.Owner, "SELECT count(*) FROM pg_views WHERE schemaname = 'aiv'")).ShouldBe(36L);

            // What the migration leaves: both roles exist, without a login or a password.
            (await ScalarAsync(db.Owner, "SELECT count(*) FROM pg_roles WHERE rolname IN ('xd_mcp_read', 'xd_mcp_sql') AND NOT rolcanlogin")).ShouldBe(2L);
            (await ScalarAsync(db.Owner, "SELECT count(*) FROM pg_authid WHERE rolname IN ('xd_mcp_read', 'xd_mcp_sql') AND rolpassword IS NOT NULL")).ShouldBe(0L);
            (await CanLoginAsync("xd_mcp_read", "anything")).ShouldBeFalse();
        }
    }
}
