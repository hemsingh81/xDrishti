using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Spec P5-04, criterion 8: <c>lab.lab_results</c> is insert-only for the runtime role, has one row per run and its columns and constraints are as designed.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabResultsGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_runtime_role_can_insert_and_read_results_and_never_update_delete_or_truncate_them()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);

        foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
        {
            await using var command = new NpgsqlCommand($"SELECT has_table_privilege('xd_app', 'lab.lab_results', '{privilege}')", connection);
            ((bool)(await command.ExecuteScalarAsync(Ct))!).ShouldBe(expected, privilege);
        }

        await using var unique = new NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE indexname = 'uq_lab_results_run'", connection);
        ((string)(await unique.ExecuteScalarAsync(Ct))!).ShouldContain("UNIQUE");
    }
}
