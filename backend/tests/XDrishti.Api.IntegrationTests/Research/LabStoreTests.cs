using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// The stored strategies against a real database as the runtime role (spec P3-02d, criteria 2, 3, 5, 6, 7 and 10): on a throw-away server where <c>xd_app</c> exists with the default privileges of the init script and the chain was applied by the
/// owner, the host connects as <c>xd_app</c>, so every statement of the store runs with the rights it has in production. The facts of the grants, the constraints, the append rules, the races, the seed and the tampered row.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabStoreTests(ApiFactory factory, ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>A version to append: the canonical text carries the tag, so each tag is another hash.</summary>
    private static NewVersion Version(string id, string tag, bool seed = false, int? expected = null, string? note = null, string style = "intraday")
    {
        var canonical = "{\"id\":\"" + id + "\",\"tag\":\"" + tag + "\"}";
        return new NewVersion(id, style, "text of " + id + " " + tag, canonical, Sha(canonical), Note: note, ExpectedVersion: expected, Seed: seed);
    }

    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", server.App));

    private static IStrategyStore StoreOf(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IStrategyStore>();

    private async Task ResetAsync() =>
        await StoreKit.ExecuteAsync(server.Owner, "DELETE FROM lab.strategy_versions; DELETE FROM lab.strategies;", Ct);

    private static async Task<string?> SqlStateAsync(string connectionString, string sql)
    {
        try
        {
            await StoreKit.ExecuteAsync(connectionString, sql, Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    private static string InsertFamily(string id) => $"INSERT INTO lab.strategies (id, style, built_in, created_at) VALUES ('{id}', 'intraday', false, now())";

    private static string InsertVersion(string id, int version, string hash, string? parent = null) =>
        $"INSERT INTO lab.strategy_versions (strategy_id, version, schema_version, hash, source_text, canonical_json, parent_hash, created_at) VALUES ('{id}', {version}, 1, '{hash}', 't', '{{}}', {(parent is null ? "NULL" : "'" + parent + "'")}, now())";

    // criterion 2

    [Fact]
    public async Task As_xd_app_select_and_insert_work_on_both_tables_and_update_delete_and_truncate_are_refused()
    {
        await ResetAsync();
        var hash = new string('a', 64);

        (await SqlStateAsync(server.App, InsertFamily("GRANTS_PROBE"))).ShouldBeNull();
        (await SqlStateAsync(server.App, InsertVersion("GRANTS_PROBE", 1, hash))).ShouldBeNull();

        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.strategies", Ct)).ShouldBe(1);
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.strategy_versions", Ct)).ShouldBe(1);
        foreach (var table in new[] { "strategies", "strategy_versions" })
        {
            (await SqlStateAsync(server.App, $"UPDATE lab.{table} SET created_at = now()")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, "update " + table);
            (await SqlStateAsync(server.App, $"DELETE FROM lab.{table}")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, "delete " + table);
            (await SqlStateAsync(server.App, $"TRUNCATE lab.{table}")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, "truncate " + table);
        }

        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.strategy_versions", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task A_malformed_hash_a_version_zero_an_id_with_a_space_a_missing_parent_and_a_clashing_id_are_refused_by_constraints()
    {
        await ResetAsync();
        var hash = new string('b', 64);
        await StoreKit.ExecuteAsync(server.App, InsertFamily("CONSTRAINT_PROBE"), Ct);

        (await SqlStateAsync(server.App, InsertVersion("CONSTRAINT_PROBE", 1, new string('B', 64)))).ShouldBe(PostgresErrorCodes.CheckViolation, "an upper-case hash");
        (await SqlStateAsync(server.App, InsertVersion("CONSTRAINT_PROBE", 1, "abc"))).ShouldBe(PostgresErrorCodes.CheckViolation, "a short hash");
        (await SqlStateAsync(server.App, InsertVersion("CONSTRAINT_PROBE", 0, hash))).ShouldBe(PostgresErrorCodes.CheckViolation, "version 0");
        (await SqlStateAsync(server.App, InsertVersion("CONSTRAINT_PROBE", 1, hash, new string('c', 64)))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation, "a parent that exists nowhere");
        (await SqlStateAsync(server.App, InsertFamily("has space"))).ShouldBe(PostgresErrorCodes.CheckViolation, "an id with a space");
        (await SqlStateAsync(server.App, InsertFamily("1STARTS_WITH_DIGIT"))).ShouldBe(PostgresErrorCodes.CheckViolation, "an id that starts with a digit");
        (await SqlStateAsync(server.App, InsertFamily("constraint_probe"))).ShouldBe(PostgresErrorCodes.UniqueViolation, "an id that differs only in case");
        (await SqlStateAsync(server.App, "UPDATE lab.strategies SET style = 'x'")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.Owner, "INSERT INTO lab.strategies (id, style, built_in, created_at) VALUES ('STYLE_PROBE', 'x', false, now())")).ShouldBe(PostgresErrorCodes.CheckViolation, "a style that is neither intraday nor swing");
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.strategy_versions", Ct)).ShouldBe(0);
    }

    // criterion 6

    [Fact]
    public async Task A_new_family_is_version_one_a_changed_text_is_version_two_with_the_first_as_parent_and_the_same_hash_is_the_same_row()
    {
        await ResetAsync();
        await using var host = Host();
        using var scope = host.Services.CreateScope();
        var store = StoreOf(scope);

        var first = await store.AppendAsync(Version("APPEND_A", "one", note: "the first"), Ct);
        var second = await store.AppendAsync(Version("APPEND_A", "two"), Ct);
        var again = await store.AppendAsync(Version("APPEND_A", "one"), Ct);
        var wrongExpectation = await store.AppendAsync(Version("APPEND_A", "one", expected: 5), Ct);

        first.Value.ShouldBe(new AppendResult("APPEND_A", 1, Version("APPEND_A", "one").Hash, true));
        second.Value.Version.ShouldBe(2);
        second.Value.Created.ShouldBeTrue();
        again.Value.ShouldBe(new AppendResult("APPEND_A", 1, Version("APPEND_A", "one").Hash, false));
        wrongExpectation.Value.Created.ShouldBeFalse("the content already has its number, whatever the caller expected");
        var v2 = await store.GetVersionAsync("APPEND_A", 2, Ct);
        v2!.ParentHash.ShouldBe(first.Value.Hash);
        v2.SchemaVersion.ShouldBe(1);
        (await store.GetVersionAsync("APPEND_A", 1, Ct))!.ParentHash.ShouldBeNull();
        (await store.GetVersionAsync("APPEND_A", 1, Ct))!.Note.ShouldBe("the first");
        (await store.GetVersionAsync("APPEND_A", null, Ct))!.Version.ShouldBe(2);
        (await store.GetByHashAsync(first.Value.Hash, Ct))!.SourceText.ShouldBe("text of APPEND_A one");
        (await store.GetVersionAsync("APPEND_A", 3, Ct)).ShouldBeNull();
        (await store.GetByHashAsync(new string('0', 64), Ct)).ShouldBeNull();
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.strategy_versions", Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task An_expected_version_that_is_not_the_next_is_a_conflict_and_writes_no_row_and_the_right_one_is_stored()
    {
        await ResetAsync();
        await using var host = Host();
        using var scope = host.Services.CreateScope();
        var store = StoreOf(scope);
        await store.AppendAsync(Version("EXPECT_A", "one"), Ct);

        var conflict = await store.AppendAsync(Version("EXPECT_A", "two", expected: 5), Ct);
        var fresh = await store.AppendAsync(Version("EXPECT_B", "one", expected: 2), Ct);
        var right = await store.AppendAsync(Version("EXPECT_A", "two", expected: 2), Ct);

        conflict.Error.Code.ShouldBe(StrategyStoreErrors.VersionExistsCode);
        conflict.Error.Kind.ShouldBe(ErrorKind.Conflict);
        fresh.Error.Code.ShouldBe(StrategyStoreErrors.VersionExistsCode, "a new family would be version 1");
        right.Value.Version.ShouldBe(2);
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.strategy_versions", Ct)).ShouldBe(2);
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.strategies", Ct)).ShouldBe(1, "the refused family was not created");
    }

    [Fact]
    public async Task A_built_in_family_is_read_only_for_everyone_but_the_seed_and_a_new_family_is_never_a_built_in_by_itself()
    {
        await ResetAsync();
        await using var host = Host();
        using var scope = host.Services.CreateScope();
        var store = StoreOf(scope);

        var seeded = await store.AppendAsync(Version("BUILT_IN_A", "one", seed: true), Ct);
        var refused = await store.AppendAsync(Version("BUILT_IN_A", "user"), Ct);
        var release = await store.AppendAsync(Version("BUILT_IN_A", "two", seed: true), Ct);
        await store.AppendAsync(Version("USER_A", "one"), Ct);

        seeded.IsSuccess.ShouldBeTrue();
        refused.Error.Code.ShouldBe(StrategyStoreErrors.BuiltInReadOnlyCode);
        release.Value.Version.ShouldBe(2);
        (await store.GetVersionAsync("BUILT_IN_A", 2, Ct))!.ParentHash.ShouldBe(seeded.Value.Hash, "the new text of a release has the old latest as its parent");
        var families = await store.ListAsync(Ct);
        families.Select(f => (f.Id, f.BuiltIn, f.LatestVersion, f.VersionCount)).ShouldBe([("BUILT_IN_A", true, 2, 2), ("USER_A", false, 1, 1)]);
        (await store.GetAsync("USER_A", Ct))!.Style.ShouldBe("intraday");
        (await store.GetAsync("NOBODY", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_hash_that_is_not_the_sha256_of_the_canonical_text_is_an_integrity_failure_and_other_unstorable_values_are_refused_before_the_database()
    {
        await ResetAsync();
        await using var host = Host();
        using var scope = host.Services.CreateScope();
        var store = StoreOf(scope);
        var good = Version("INTEGRITY_A", "one");

        (await store.AppendAsync(good with { Hash = new string('a', 64) }, Ct)).Error.Code.ShouldBe(StrategyStoreErrors.IntegrityFailureCode);
        (await store.AppendAsync(good with { CanonicalJson = "{}" }, Ct)).Error.Code.ShouldBe(StrategyStoreErrors.IntegrityFailureCode);
        (await store.AppendAsync(good with { StrategyId = "has space" }, Ct)).Error.Code.ShouldBe(StrategyStoreErrors.InvalidCode);
        (await store.AppendAsync(good with { Style = "weekly" }, Ct)).Error.Code.ShouldBe(StrategyStoreErrors.InvalidCode);
        (await store.AppendAsync(good with { Note = new string('n', 501) }, Ct)).Error.Code.ShouldBe(StrategyStoreErrors.InvalidCode);
        (await store.AppendAsync(good with { SourceText = new string('x', 262_145) }, Ct)).Error.Code.ShouldBe(StrategyStoreErrors.InvalidCode);
        (await store.AppendAsync(good with { Hash = "ABC" }, Ct)).Error.Code.ShouldBe(StrategyStoreErrors.InvalidCode);
        (await store.AppendAsync(good with { ParentHash = new string('9', 64) }, Ct)).Error.Code.ShouldBe(StrategyStoreErrors.InvalidCode, "a parent that is no stored version");
        (await store.AppendAsync(good with { Note = new string('n', 500), SourceText = new string('x', 262_144) }, Ct)).IsSuccess.ShouldBeTrue("the limits themselves are allowed");
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.strategy_versions", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task An_id_that_differs_from_a_stored_one_only_in_case_is_refused_and_a_parent_from_another_family_is_a_lineage()
    {
        await ResetAsync();
        await using var host = Host();
        using var scope = host.Services.CreateScope();
        var store = StoreOf(scope);
        var orb = await store.AppendAsync(Version("ORB_CASE", "one", seed: true), Ct);

        var clash = await store.AppendAsync(Version("orb_case", "one-lower"), Ct);
        var clone = await store.AppendAsync(Version("ORB_CLONE", "one") with { ParentHash = orb.Value.Hash }, Ct);

        clash.Error.Code.ShouldBe(StrategyStoreErrors.IdClashCode);
        clone.Value.Version.ShouldBe(1);
        (await store.GetVersionAsync("ORB_CLONE", 1, Ct))!.ParentHash.ShouldBe(orb.Value.Hash);
        (await store.GetAsync("orb_case", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task One_family_is_read_by_its_id_with_its_counts_and_its_versions_in_order()
    {
        await ResetAsync();
        await using var host = Host();
        using var scope = host.Services.CreateScope();
        var store = StoreOf(scope);
        await store.AppendAsync(Version("FAM_A", "one"), Ct);
        await store.AppendAsync(Version("FAM_A", "two"), Ct);
        await store.AppendAsync(Version("FAM_B", "one", style: "swing"), Ct);

        var a = await store.GetAsync("FAM_A", Ct);
        var versions = await store.ListVersionsAsync("FAM_A", Ct);

        a.ShouldNotBeNull();
        a.LatestVersion.ShouldBe(2);
        a.VersionCount.ShouldBe(2);
        (await store.GetAsync("FAM_B", Ct))!.Style.ShouldBe("swing");
        (await store.GetAsync("FAM_C", Ct)).ShouldBeNull();
        versions.Select(v => v.Version).ShouldBe([1, 2]);
        (await store.ListVersionsAsync("FAM_C", Ct)).ShouldBeEmpty();
    }

    // criterion 7

    [Fact]
    public async Task Eight_concurrent_appends_of_the_same_new_text_give_one_row_and_one_version_for_all()
    {
        await ResetAsync();
        await using var host = Host();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var scope = host.Services.CreateScope();
            return await StoreOf(scope).AppendAsync(Version("RACE_SAME", "one"), Ct);
        }));

        results.ShouldAllBe(r => r.IsSuccess && r.Value.Version == 1);
        results.Count(r => r.Value.Created).ShouldBe(1);
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.strategy_versions WHERE strategy_id = 'RACE_SAME'", Ct)).ShouldBe(1);
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.strategies WHERE id = 'RACE_SAME'", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Eight_concurrent_appends_of_eight_texts_to_one_family_give_versions_one_to_eight_once_each()
    {
        await ResetAsync();
        await using var host = Host();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            using var scope = host.Services.CreateScope();
            return await StoreOf(scope).AppendAsync(Version("RACE_MANY", "text-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), Ct);
        }));

        results.ShouldAllBe(r => r.IsSuccess && r.Value.Created);
        results.Select(r => r.Value.Version).Order().ShouldBe([1, 2, 3, 4, 5, 6, 7, 8]);
        var parents = await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.strategy_versions WHERE strategy_id = 'RACE_MANY' AND (parent_hash IS NULL) = (version = 1)", Ct);
        parents.ShouldBe(8, "every version but the first has a parent");
    }

    // criterion 3

    [Fact]
    public async Task The_seed_stores_three_built_in_families_at_version_one_and_a_second_run_writes_nothing()
    {
        await ResetAsync();
        await using var host = Host();

        (await StrategySeeding.SeedAsync(host.Services, Ct)).ShouldBe(3);
        var before = await ReadRowsAsync();
        (await StrategySeeding.SeedAsync(host.Services, Ct)).ShouldBe(3);
        var after = await ReadRowsAsync();

        before.Count.ShouldBe(3);
        before.Select(r => r.Split('|')[0]).ShouldBe(["BASE_BREAKOUT", "ORB", "VWAP_PULLBACK"]);
        before.Select(r => r.Split('|')[1] + "|" + r.Split('|')[2] + "|" + r.Split('|')[3]).Distinct().ShouldBe(["true|1|"], "built in, version 1, no parent");
        after.ShouldBe(before, "a restart changes nothing, not even a created_at");
        using var scope = host.Services.CreateScope();
        (await StoreOf(scope).GetVersionAsync("ORB", null, Ct))!.SourceText.ShouldContain("id: ORB");
    }

    private async Task<List<string>> ReadRowsAsync()
    {
        await using var connection = new NpgsqlConnection(server.App);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT s.id || '|' || s.built_in || '|' || v.version || '|' || coalesce(v.parent_hash, '') || '|' || s.created_at || '|' || v.created_at || '|' || v.hash FROM lab.strategies s JOIN lab.strategy_versions v ON v.strategy_id = s.id ORDER BY s.id, v.version", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    // criterion 10

    [Fact]
    public async Task A_stored_text_changed_by_the_owner_is_refused_when_loaded_and_nothing_runs()
    {
        await ResetAsync();
        await using var host = Host();
        await StrategySeeding.SeedAsync(host.Services, Ct);
        await StoreKit.ExecuteAsync(server.Owner, "UPDATE lab.strategy_versions SET source_text = replace(source_text, 'or_min: 15', 'or_min: 45') WHERE strategy_id = 'ORB'", Ct);
        using var scope = host.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
        var request = new SimulationRequest("ORB", null, "15m", new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4), [Guid.CreateVersion7()], null, CostChoice.Gross);

        var result = await handler.HandleAsync(new RunSimulationQuery(request), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("strategy_store.hash_mismatch");
    }
}
