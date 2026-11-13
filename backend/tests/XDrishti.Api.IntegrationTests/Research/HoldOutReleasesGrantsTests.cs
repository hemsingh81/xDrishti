using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>HO-01 (ADR 0283): <c>lab.holdout_releases</c> is insert-only for the runtime role, and the block table stays insert-only.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldOutReleasesGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("lab.holdout_releases")]
    [InlineData("lab.holdout_blocks")]
    public async Task The_runtime_role_can_insert_and_read_and_never_update_delete_or_truncate(string table)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);

        foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
        {
            await using var command = new NpgsqlCommand($"SELECT has_table_privilege('xd_app', '{table}', '{privilege}')", connection);
            ((bool)(await command.ExecuteScalarAsync(Ct))!).ShouldBe(expected, $"{table} {privilege}");
        }
    }
}
