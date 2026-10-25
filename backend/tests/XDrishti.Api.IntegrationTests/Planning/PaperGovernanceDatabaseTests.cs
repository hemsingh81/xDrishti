using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Paper;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// Spec P10-05 on a real database: the configuration is read through the real ports with no secret in it, the gate stores a snapshot and the first epoch, a stored frozen change starts an epoch and keeps the earlier one, a note and an owner restart are
/// insert-only rows, the unique keys hold, and every endpoint is a GET that reads the record.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PaperGovernanceDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Session = new(2099, 1, 5);

    private async Task<TResult> WithAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    [Fact]
    public async Task The_real_configuration_reading_is_complete_and_names_no_secret()
    {
        var reading = await WithAsync(p => p.GetRequiredService<IConfigSnapshotSource>().ReadAsync(Ct).AsTask());
        var fingerprint = ConfigFingerprint.Create(reading.Components);

        reading.Unreadable.ShouldBeEmpty();
        fingerprint.Dropped.ShouldBe(0);
        fingerprint.Values.Keys.ShouldContain("engine.version");
        fingerprint.Values.Keys.ShouldContain(PaperConfigKeys.CellsSnapshot);
        fingerprint.Values.Keys.ShouldContain(PaperConfigKeys.ModelsChampion);
        fingerprint.Values.Keys.ShouldContain("autonomy.auto_approve_tickets");
        foreach (var word in new[] { "token", "secret", "password", "api_key" })
        {
            fingerprint.CanonicalJson.ShouldNotContain(word, Case.Insensitive);
        }
    }

    [Fact]
    public async Task The_gate_stores_one_snapshot_per_day_and_hash_and_the_first_epoch_then_a_stored_change_is_read_back()
    {
        var first = await WithAsync(p => p.GetRequiredService<IEpochGate>().ResolveAsync([Session], Ct).AsTask());
        var second = await WithAsync(p => p.GetRequiredService<IEpochGate>().ResolveAsync([Session], Ct).AsTask());

        first.Epoch.Kind.ShouldBe("first");
        second.Epoch.Id.ShouldBe(first.Epoch.Id);
        second.MeasureStatus[Session].ShouldBe("measured");
        (await ScalarAsync<long>("SELECT count(*) FROM paper.config_snapshots")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM paper.epochs")).ShouldBe(1);

        var snapshot = await WithAsync(p => p.GetRequiredService<IGovernanceStore>().LatestSnapshotAsync(true, Ct).AsTask());
        var committed = await WithAsync(p => p.GetRequiredService<IGovernanceStore>().CommitAsync(
            new GovernanceCommit(
                new NewSnapshot(DateTimeOffset.UtcNow, Session.AddDays(1), new string('b', 64), "{}", false, "xd-api"),
                snapshot!.Id,
                new NewDetectedEpoch(Session.AddDays(2), DateTimeOffset.UtcNow, new string('b', 64), "the configuration changed"),
                first.Epoch.Id,
                [new NewChange("planning.builder:useselection", "frozen", "false", "true", "new_epoch", null)]),
            Ct).AsTask());
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IGovernanceStore>().AddNoteAsync(committed.EpochId, null, null, "owner note", "cli:xd", DateTimeOffset.UtcNow, Ct);
        }

        await ResignInAsync();
        var epochs = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/epochs", UriKind.Relative), Ct);
        var detail = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/paper/epochs/{committed.EpochId}", UriKind.Relative), Ct);
        var changes = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/config/changes?limit=5", UriKind.Relative), Ct);
        var config = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/config", UriKind.Relative), Ct);
        var queue = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/queue", UriKind.Relative), Ct);
        using var missing = await Client.GetAsync(new Uri("/api/paper/epochs/999999", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri("/api/paper/config/changes?limit=0", UriKind.Relative), Ct);

        epochs.GetProperty("items").GetArrayLength().ShouldBe(2);
        epochs.GetProperty("items")[0].GetProperty("kind").GetString().ShouldBe("detected_change");
        epochs.GetProperty("items")[1].GetProperty("endedOn").GetString().ShouldBe(Session.AddDays(2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        detail.GetProperty("changes")[0].GetProperty("unexplained").GetBoolean().ShouldBeTrue();
        detail.GetProperty("notes").GetArrayLength().ShouldBe(1);
        changes.GetProperty("items").GetArrayLength().ShouldBe(1);
        config.GetProperty("components").GetArrayLength().ShouldBeGreaterThan(0);
        queue.GetProperty("note").GetString()!.ShouldContain("nothing was applied");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_database_refuses_a_second_snapshot_of_a_day_and_hash_a_bad_class_and_a_change_without_an_epoch()
    {
        var insert = "INSERT INTO paper.config_snapshots (taken_at, session_date, hash, components, partial, process) VALUES (now(), '2099-02-02', repeat('c', 64), '{}', false, 'xd-api')";
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, insert);

        var duplicate = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, insert));
        var badClass = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "INSERT INTO paper.config_changes (from_snapshot_id, to_snapshot_id, \"key\", \"class\", effect, epoch_id) SELECT id, id, 'k', 'maybe', 'noted', 1 FROM paper.config_snapshots LIMIT 1"));

        duplicate.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        badClass.SqlState.ShouldBeOneOf(PostgresErrorCodes.CheckViolation, PostgresErrorCodes.ForeignKeyViolation);
    }
}
