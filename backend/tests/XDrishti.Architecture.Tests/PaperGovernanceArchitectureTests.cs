using System.Reflection;
using XDrishti.Application.Abstractions.Paper;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P10-05 (ADR 0119): nothing here applies a change by itself. The governance code only reads the learning, autonomy, account and strategy stores; it calls no writer of them, the HTTP surface is GET only, and the governance store only reads and inserts.
/// </summary>
public sealed class PaperGovernanceArchitectureTests
{
    private static readonly string[] Files =
    [
        "FingerprintEpochGate.cs", "ConfigSnapshotSource.cs", "GovernanceQueryHandlers.cs", "GovernanceCommandHandlers.cs", "GovernanceViews.cs",
    ];

    private static string Application(string file) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", "XDrishti.Application", "Paper", file]));

    [Fact]
    public void The_governance_code_calls_no_writer_of_the_stores_it_reads()
    {
        string[] writers =
        [
            "IHypothesisLog.", ".ProposeAsync(", ".AppendAsync(", ".DesignateAsync(", ".RegisterAsync(", ".StoreAsync(", ".SaveAsync(", ".MoveItemAsync(", ".MovePlanAsync(", ".PublishAsync(", ".WriteSessionAsync(",
            "EnsureFirstEpochAsync(", "ChangeAutonomySettings", "IServiceScheduleStore", "SetEnabled", "IBrokerConnector", "IConfigurationRoot",
        ];

        foreach (var file in Files)
        {
            var text = Application(file);
            foreach (var writer in writers)
            {
                text.ShouldNotContain(writer, customMessage: file);
            }
        }
    }

    [Fact]
    public void Only_the_two_owner_handlers_write_and_only_to_the_governance_store()
    {
        Application("GovernanceQueryHandlers.cs").ShouldNotContain("StartOwnerEpochAsync");
        Application("GovernanceQueryHandlers.cs").ShouldNotContain("AddNoteAsync");
        Application("GovernanceQueryHandlers.cs").ShouldNotContain("CommitAsync");
        Application("FingerprintEpochGate.cs").ShouldNotContain("StartOwnerEpochAsync");
        Application("FingerprintEpochGate.cs").ShouldNotContain("AddNoteAsync");
    }

    [Fact]
    public void The_governance_endpoints_are_get_only()
    {
        var text = File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", "XDrishti.Api", "Endpoints", "PaperGovernanceEndpoints.cs"]));

        foreach (var verb in new[] { "MapPost", "MapPut", "MapDelete", "MapPatch", "MapMethods" })
        {
            text.ShouldNotContain(verb);
        }

        text.ShouldContain("MapGet");
    }

    [Fact]
    public void The_governance_store_port_reads_and_inserts_and_never_updates_or_deletes()
    {
        typeof(IGovernanceStore).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Order()
            .ShouldBe(["AddNoteAsync", "CommitAsync", "CreateFirstEpochAsync", "FindChangeAsync", "LatestSnapshotAsync", "ListChangesAsync", "ListEpochsAsync", "ListNotesAsync", "StartOwnerEpochAsync", "UsedHypothesisIdsAsync"]);
    }

    [Fact]
    public void The_snapshot_source_port_only_reads()
    {
        typeof(IConfigSnapshotSource).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ShouldBe(["ReadAsync"]);
    }
}
