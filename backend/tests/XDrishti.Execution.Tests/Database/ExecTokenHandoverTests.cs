using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using XDrishti.Domain.Accounts;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>
/// ADR 0171 steps 1, 3 and 4 on a real database, as the real roles: the hand-over table is written by xd_app only and read by xd_exec only; the execution service's reader gives the token only while the connection is Connected with an expiry after now and
/// the row opens for that account and name with the hand-over key; every other case is unavailable. Nothing it logs holds the token, the key or the account id. Values are made up.
/// </summary>
[Collection(ExecDatabaseSuite.Name)]
public sealed class ExecTokenHandoverTests(ExecDatabase db)
{
    private const string Marker = "FAKE-LIVE-" + "TOKEN-DO-NOT-LEAK";

    private const string HandKey = "test-handover-" + "key-0123456789abcdefghijkl";

    private const string RotatedKey = "rotated-handover-" + "key-0123456789abcdefghij";

    private static readonly DateTimeOffset Now = new(2026, 10, 31, 3, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SecretValue Secret(string text) => SecretValue.TryCreate(text, out var value, out _) ? value : throw new InvalidOperationException("invalid test value");

    private static byte[] Seal(Guid account, string name = "access_token", SecretCipherPurpose purpose = SecretCipherPurpose.ExecHandover) =>
        new SecretCipher(HandKey, purpose).Encrypt(account, name, Secret(Marker));

    private sealed class ListLogger : ILogger<PgExecTokenHandover>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }

    // A fresh account of its own (the tests of the suite share the server, not data) with a connection in the given status and expiry.
    private async Task<Guid> AccountAsync(string status = "Connected", TimeSpan? expiresIn = null)
    {
        var account = Guid.NewGuid();
        var slug = "handover-" + account.ToString("N")[..12];
        await ExecDatabase.ExecuteAsync(db.Owner, $"""
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES ('{account}', '{slug}', '{slug}', 'Dhan', '1000000001', current_date, '#112233', now(), now(), false, false, false, 0);
            INSERT INTO accounts.broker_connections (account_id, status, expires_at) VALUES ('{account}', '{status}', '{(Now + (expiresIn ?? TimeSpan.FromHours(10))).UtcDateTime:O}')
            """);
        return account;
    }

    // The hand-over row, written as the API's role.
    private async Task HandOverAsync(Guid account, byte[] sealedToken, int keyVersion = 1)
    {
        await using var connection = new NpgsqlConnection(db.App);
        await connection.OpenAsync(Ct);
        await using var insert = new NpgsqlCommand("INSERT INTO accounts.exec_token_handover (account_id, sealed_token, key_version, created_at) VALUES (@a, @s, @v, now())", connection);
        insert.Parameters.AddWithValue("a", account);
        insert.Parameters.AddWithValue("s", sealedToken);
        insert.Parameters.AddWithValue("v", keyVersion);
        await insert.ExecuteNonQueryAsync(Ct);
    }

    private async Task<(string? Token, ListLogger Log)> FindAsync(Guid account, string key = HandKey)
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var log = new ListLogger();
        var source = new PgExecTokenHandover(data, Options.Create(new ExecHandoverKeyOptions { Key = key }), new FakeTimeProvider(Now), log);
        return ((await source.FindAsync(account, Ct))?.Reveal(), log);
    }

    [Fact]
    public async Task A_connected_unexpired_account_gets_its_own_token()
    {
        var account = await AccountAsync();
        await HandOverAsync(account, Seal(account));

        var (token, log) = await FindAsync(account);

        token.ShouldBe(Marker);
        log.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_row_moved_from_another_account_does_not_open()
    {
        var account = await AccountAsync();
        await HandOverAsync(account, Seal(Guid.NewGuid()));

        (await FindAsync(account)).Token.ShouldBeNull();
    }

    [Fact]
    public async Task A_row_sealed_under_another_name_does_not_open()
    {
        var account = await AccountAsync();
        await HandOverAsync(account, Seal(account, name: "api_key"));

        (await FindAsync(account)).Token.ShouldBeNull();
    }

    [Fact]
    public async Task A_row_sealed_with_the_vault_purpose_does_not_open()
    {
        var account = await AccountAsync();
        await HandOverAsync(account, Seal(account, purpose: SecretCipherPurpose.BrokerVault));

        (await FindAsync(account)).Token.ShouldBeNull();
    }

    [Theory]
    [InlineData("expired", "Connected")]
    [InlineData("disconnected", "NotConnected")]
    [InlineData("unknown key version", "Connected")]
    [InlineData("no row", "Connected")]
    public async Task Anything_but_a_connected_unexpired_decryptable_row_is_unavailable(string scene, string status)
    {
        var account = await AccountAsync(status, expiresIn: scene == "expired" ? TimeSpan.Zero : null);
        if (scene != "no row")
        {
            await HandOverAsync(account, Seal(account), scene == "unknown key version" ? 2 : 1);
        }

        (await FindAsync(account)).Token.ShouldBeNull(scene);
    }

    [Fact]
    public async Task After_a_key_rotation_the_row_is_unavailable_and_the_warning_holds_no_token_key_or_account()
    {
        var account = await AccountAsync();
        await HandOverAsync(account, Seal(account));

        var (token, log) = await FindAsync(account, RotatedKey);

        token.ShouldBeNull();
        log.Lines.ShouldHaveSingleItem().ShouldContain("execution.token_handover_undecryptable");
        foreach (var line in log.Lines)
        {
            line.ShouldNotContain(Marker);
            line.ShouldNotContain(HandKey);
            line.ShouldNotContain(RotatedKey);
            line.ShouldNotContain(account.ToString());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short-0123456789")]
    public async Task Without_a_usable_key_nothing_opens_and_one_warning_without_the_key_is_logged(string key)
    {
        var account = await AccountAsync();
        await HandOverAsync(account, Seal(account));

        var (token, log) = await FindAsync(account, key);

        token.ShouldBeNull();
        log.Lines.ShouldHaveSingleItem().ShouldContain("execution.token_handover_no_key");
        if (key.Length > 0)
        {
            log.Lines[0].ShouldNotContain(key);
        }
    }

    [Fact]
    public async Task The_app_role_writes_the_hand_over_and_the_execution_role_only_reads_it()
    {
        var account = await AccountAsync();
        var insert = $"INSERT INTO accounts.exec_token_handover (account_id, sealed_token, key_version, created_at) VALUES ('{account}', '\\x00', 1, now())";

        (await ExecDatabase.SqlStateAsync(db.Exec, insert)).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, insert)).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.App, $"UPDATE accounts.exec_token_handover SET key_version = 1 WHERE account_id = '{account}'")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.App, "SELECT sealed_token FROM accounts.exec_token_handover")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, "SELECT account_id, sealed_token, key_version, created_at FROM accounts.exec_token_handover")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, $"UPDATE accounts.exec_token_handover SET key_version = 2 WHERE account_id = '{account}'")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.Exec, $"DELETE FROM accounts.exec_token_handover WHERE account_id = '{account}'")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.Exec, "TRUNCATE accounts.exec_token_handover")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, $"DELETE FROM accounts.exec_token_handover WHERE account_id = '{account}'")).ShouldBeNull();
    }
}
