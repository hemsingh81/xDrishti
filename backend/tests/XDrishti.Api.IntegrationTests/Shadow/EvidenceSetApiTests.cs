using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Shadow;

/// <summary>
/// AM-01b (GAP-09): the evidence set of the current edge epoch is readable with a session, replaceable only by the owner with a fresh password and a reason, only with stored versions, and never once a shadow row of the epoch exists (409). The row is stamped by the database,
/// the audit event carries no reason, and the versions are registered in the trial ledger as a registration, not a look.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class EvidenceSetApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Route = new("/api/paper/shadow/evidence-set", UriKind.Relative);

    private const string AuditProbe = "reason-text-that-the-audit-feed-must-not-carry";

    private string _strategy = string.Empty;

    private string _hash = string.Empty;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _strategy = "EVSET" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        _hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_strategy)));
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM accounts.account_events WHERE event = 'EvidenceSetReplaced'");
        await ExecuteAsync(connection, "INSERT INTO lab.strategies (id, style, built_in, created_at) VALUES (@id, 'intraday', false, now())", ("id", _strategy));
        await ExecuteAsync(
            connection,
            "INSERT INTO lab.strategy_versions (strategy_id, version, schema_version, hash, source_text, canonical_json, created_at) VALUES (@id, 1, 1, @hash, 't', '{}', now())",
            ("id", _strategy), ("hash", _hash));
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(string? password, string[]? versions = null, string? reason = AuditProbe)
    {
        using var response = await Client.PutAsJsonAsync(Route, new { versions = versions ?? [_hash], reason, password, actor = "owner:evil@example.test" }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task Table_B_ML_switches_read_OFF_with_nothing_stored()
    {
        using var scope = Factory.Services.CreateScope();

        var switches = await scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Learning.IMlSwitchSettings>().CurrentAsync(Ct);

        switches.ShouldBe(XDrishti.Application.Abstractions.Learning.MlSwitches.AllOff);
    }

    [Fact]
    public async Task Without_a_session_both_routes_are_401()
    {
        using var read = await Factory.CreateAnonymousClient().GetAsync(Route, Ct);
        using var write = await Factory.CreateAnonymousClient().PutAsJsonAsync(Route, new { versions = new[] { _hash }, reason = "x", password = Factory.OwnerPassword }, Ct);

        (read.StatusCode, write.StatusCode).ShouldBe((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task With_nothing_stored_the_set_is_pending_and_names_no_version_without_a_snapshot()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "DELETE FROM paper.config_snapshots");
        }

        var view = await Client.GetFromJsonAsync<JsonElement>(Route, Ct);

        view.GetProperty("state").GetString().ShouldBe("pending");
        view.GetProperty("versions").GetArrayLength().ShouldBe(0);
        view.GetProperty("mode").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-password")]
    public async Task A_missing_or_wrong_password_is_401_and_nothing_is_stored(string? password)
    {
        var result = await PutAsync(password);

        result.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await ScalarAsync<long>("SELECT count(*) FROM paper.evidence_sets")).ShouldBe(0);
    }

    [Fact]
    public async Task A_version_that_is_not_stored_and_a_malformed_one_are_400()
    {
        var unknown = await PutAsync(Factory.OwnerPassword, [new string('e', 64)]);
        var malformed = await PutAsync(Factory.OwnerPassword, ["nothex"]);
        var noReason = await PutAsync(Factory.OwnerPassword, reason: " ");

        unknown.Body.GetProperty("code").GetString().ShouldBe("paper.evidence_set_unknown_version");
        malformed.Body.GetProperty("code").GetString().ShouldBe("paper.evidence_set_versions_invalid");
        noReason.Body.GetProperty("code").GetString().ShouldBe("paper.evidence_set_reason_invalid");
        (await ScalarAsync<long>("SELECT count(*) FROM paper.evidence_sets")).ShouldBe(0);
    }

    [Fact]
    public async Task The_owner_replaces_the_set_before_the_first_write_it_is_open_audited_without_the_reason_and_registered_in_the_ledger()
    {
        var result = await PutAsync(Factory.OwnerPassword);

        result.Status.ShouldBe(HttpStatusCode.OK);
        (await ScalarAsync<string>("SELECT mode || '/' || actor FROM paper.evidence_sets")).ShouldStartWith("owner/owner:");
        (await ScalarAsync<string>("SELECT actor FROM paper.evidence_sets")).ShouldNotContain("evil");
        var view = await Client.GetFromJsonAsync<JsonElement>(Route, Ct);
        (view.GetProperty("state").GetString(), view.GetProperty("mode").GetString()).ShouldBe(("open", "owner"));
        view.GetProperty("versions")[0].GetString().ShouldBe(_hash);
        (await ScalarAsync<long>("SELECT count(*) FROM lab.test_ledger WHERE trial_kind = 'evidence_set' AND strategy_id = @id AND strategy_hash = @h", ("id", _strategy), ("h", _hash))).ShouldBe(1);
        var detail = await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'EvidenceSetReplaced'");
        detail.ShouldNotContain(AuditProbe);
    }

    [Fact]
    public async Task A_second_replace_before_the_first_write_stands_and_the_newest_row_is_current()
    {
        (await PutAsync(Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);
        (await PutAsync(Factory.OwnerPassword, [_hash], "again")).Status.ShouldBe(HttpStatusCode.OK);

        (await ScalarAsync<long>("SELECT count(*) FROM paper.evidence_sets")).ShouldBe(2);
        (await Client.GetFromJsonAsync<JsonElement>(Route, Ct)).GetProperty("reason").GetString().ShouldBe("again");
    }

    [Fact]
    public async Task Once_a_shadow_row_of_the_epoch_exists_the_replace_is_409_frozen_and_nothing_is_stored()
    {
        await using (var connection = await OpenAsync())
        {
            var plan = await ScalarAsync<long>(
                "INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at) VALUES (@k, '2099-03-04', '2099-03-04 03:35:00+00', 1, repeat('a', 64), 'b', true, 'n', 'l', 'probe', '1999-12-31 00:00:00+00') RETURNING id",
                ("k", "evs-" + Guid.NewGuid().ToString("N")));
            var item = await ScalarAsync<long>(
                "INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable) VALUES (@p, 1, 'primary', 's', 'BUY', 'DEC', 'h', 't', 's', 't', 'v', '{}', 'c', 1, 'd', 'b', '{}') RETURNING id",
                ("p", plan));
            await ExecuteAsync(connection, "INSERT INTO paper.shadow_sessions (session_date, n_rows, status, digest, prev_digest, written_at) VALUES ('2099-03-04', 1, 'ok', repeat('a', 64), NULL, '2000-01-01')");
            await ExecuteAsync(
                connection,
                "INSERT INTO paper.shadow_entries (session_date, plan_id, plan_item_id, trade_key, ticket_group, cell_key, strategy_hash, symbol, side, time_rule, counts_for_edge, written_at, row_hash) VALUES ('2099-03-04', @p, @i, @k, 'primary', 'c', repeat('a', 64), 's', 'BUY', 'v', true, '2000-01-01', repeat('b', 64))",
                ("p", plan), ("i", item), ("k", "DEC|S|Buy|2099-03-04|" + Guid.NewGuid().ToString("N")));
        }

        var result = await PutAsync(Factory.OwnerPassword);

        result.Status.ShouldBe(HttpStatusCode.Conflict);
        result.Body.GetProperty("code").GetString().ShouldBe("paper.evidence_set_frozen");
        (await ScalarAsync<long>("SELECT count(*) FROM paper.evidence_sets")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'EvidenceSetReplaced'")).ShouldBe(0);
    }
}
