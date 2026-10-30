using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>
/// Spec P12-09 criterion 9 and the Contract over HTTP on a real database: the limits sheet is read and changed only with a session; a raise needs the password and waits for the next session after a full closed one; a pure lowering needs
/// only the session and is in force at once; an illegal value is 400 naming the field. The limits table is insert-only, so each test has its own account.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExecutionLimitsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await ResignInAsync();
    }

    private async Task<Guid> AccountAsync()
    {
        var id = Guid.NewGuid();
        var client = Random.Shared.NextInt64(2_000_000_000, 9_000_000_000);
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget) VALUES (@id, @slug, @name, 'Dhan', @client, current_date, '#112233', now(), now(), false, false, false, 0)",
            ("id", id), ("slug", "l-" + id.ToString("N")), ("name", "Limits " + id.ToString("N")[..8]), ("client", client.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await ExecuteAsync(connection, "INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments) VALUES (@id, true, 'ManualLive', 'Fixed', 100000, true, false, 1, 0, 0)", ("id", id));
        await ExecuteAsync(connection, "INSERT INTO accounts.portfolio_settings (account_id, enabled, include_in_totals) VALUES (@id, false, true)", ("id", id));
        return id;
    }

    /// <summary>A sheet in force now, written as the owner past the raise rule (the only way to have one within a test).</summary>
    private async Task InForceAsync(Guid account)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "ALTER TABLE execution.limits DISABLE TRIGGER trg_exec_limits_check");
        try
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO execution.limits (account_id, max_orders_per_day, max_notional_per_order, max_notional_per_day, max_loss_per_day, max_open_positions, allow_list, window_start, window_end, products, actor, reason, effective_from, snapshot_hash) VALUES (@a, 6, 60000, 150000, 1500, 3, '[\"TATAPOWER\",\"INFY\",\"SBIN\"]', '09:20', '14:45', ARRAY['EQ_MIS'], 'owner:test', 'r', now() - interval '1 minute', repeat('a', 64))",
                ("a", account));
        }
        finally
        {
            await ExecuteAsync(connection, "ALTER TABLE execution.limits ENABLE TRIGGER trg_exec_limits_check");
        }
    }

    private static Uri Limits(Guid account) => new($"/api/execution/limits/{account}", UriKind.Relative);

    private static object Body(int orders = 6, string[]? allow = null, string start = "09:20", string? password = null) => new
    {
        maxOrdersPerDay = orders,
        maxNotionalPerOrder = 60000m,
        maxNotionalPerDay = 150000m,
        maxLossPerDay = 1500m,
        maxOpenPositions = 3,
        allowList = allow ?? ["TATAPOWER", "INFY", "SBIN"],
        windowStart = start,
        windowEnd = "14:45",
        products = new[] { "EQ_MIS" },
        exitAllOnLossHalt = false,
        reason = "the owner's sheet",
        password,
    };

    private async Task<(HttpStatusCode Status, JsonElement Json)> PutAsync(Guid account, object body, HttpClient? client = null)
    {
        using var response = await (client ?? Client).PutAsJsonAsync(Limits(account), body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task Without_a_session_the_limits_routes_are_401()
    {
        var anonymous = Factory.CreateAnonymousClient();
        var account = Guid.NewGuid();

        using var read = await anonymous.GetAsync(new Uri($"/api/execution/limits?accountId={account}", UriKind.Relative), Ct);
        var write = await PutAsync(account, Body(password: Factory.OwnerPassword), anonymous);

        read.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        write.Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_new_account_has_no_sheet_which_allows_nothing_and_shows_the_ceilings()
    {
        var account = await AccountAsync();

        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/execution/limits?accountId={account}", UriKind.Relative), Ct);
        using var missing = await Client.GetAsync(new Uri("/api/execution/limits", UriKind.Relative), Ct);
        using var unknown = await Client.GetAsync(new Uri($"/api/execution/limits?accountId={Guid.NewGuid()}", UriKind.Relative), Ct);

        view.GetProperty("inForce").ValueKind.ShouldBe(JsonValueKind.Null);
        view.GetProperty("complete").GetBoolean().ShouldBeFalse();
        view.GetProperty("missing").GetArrayLength().ShouldBe(8);
        view.GetProperty("ceilings").GetProperty("maxOrdersPerDay").GetInt32().ShouldBe(20);
        view.GetProperty("ceilings").GetProperty("windowLatest").GetString().ShouldBe("15:00");
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_raise_needs_the_password_and_is_pending_until_09_00_after_a_full_closed_session()
    {
        var account = await AccountAsync();

        var without = await PutAsync(account, Body());
        var stored = await PutAsync(account, Body(password: Factory.OwnerPassword));

        without.Status.ShouldBe(HttpStatusCode.BadRequest);
        without.Json.GetProperty("code").GetString().ShouldBe("execution.password_required");
        stored.Status.ShouldBe(HttpStatusCode.OK);
        stored.Json.GetProperty("inForce").ValueKind.ShouldBe(JsonValueKind.Null);
        var pending = stored.Json.GetProperty("pending");
        pending.GetProperty("maxOrdersPerDay").GetInt32().ShouldBe(6);
        var effective = pending.GetProperty("effectiveFrom").GetDateTimeOffset().ToOffset(TimeSpan.FromMinutes(330));
        effective.TimeOfDay.ShouldBe(new TimeSpan(9, 0, 0));
        effective.ShouldBeGreaterThan(stored.Json.GetProperty("serverNow").GetDateTimeOffset().AddHours(12));
    }

    [Fact]
    public async Task A_pure_lowering_needs_only_the_session_and_is_in_force_at_once()
    {
        var account = await AccountAsync();
        await InForceAsync(account);

        var lowered = await PutAsync(account, Body(orders: 3, allow: ["INFY"], start: "10:00"));

        lowered.Status.ShouldBe(HttpStatusCode.OK);
        lowered.Json.GetProperty("inForce").GetProperty("maxOrdersPerDay").GetInt32().ShouldBe(3);
        lowered.Json.GetProperty("inForce").GetProperty("windowStart").GetString().ShouldBe("10:00");
        lowered.Json.GetProperty("pending").ValueKind.ShouldBe(JsonValueKind.Null);
        lowered.Json.GetProperty("complete").GetBoolean().ShouldBeTrue();
        lowered.Json.GetProperty("history").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task An_illegal_value_is_400_values_invalid_naming_the_field()
    {
        var account = await AccountAsync();

        var orders = await PutAsync(account, Body(orders: 21, password: Factory.OwnerPassword));
        var window = await PutAsync(account, Body(start: "09:15", password: Factory.OwnerPassword));

        orders.Status.ShouldBe(HttpStatusCode.BadRequest);
        orders.Json.GetProperty("code").GetString().ShouldBe("execution.values_invalid");
        orders.Json.GetProperty("title").GetString()!.ShouldStartWith("maxOrdersPerDay");
        window.Json.GetProperty("title").GetString()!.ShouldStartWith("window");
    }
}
