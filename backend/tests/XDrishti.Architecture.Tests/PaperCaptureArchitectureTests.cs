using System.Reflection;
using XDrishti.Application.Abstractions.Paper;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P10-01 (ADR 0116): the paper capture has one replay path with grading and cannot move a ticket. Grading and capture never build a simulation request themselves (only the replayer does), the capture code names no ticket move,
/// and the paper port only reads and inserts. The no-order tripwire (<see cref="NoOrderPlacementArchitectureTests"/>) covers the new types as well.
/// </summary>
public sealed class PaperCaptureArchitectureTests
{
    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", "XDrishti.Application", .. parts]));

    [Fact]
    public void Only_the_replayer_builds_a_simulation_query_for_a_ticket()
    {
        Source("Planning", "TicketReplayer.cs").ShouldContain("new RunSimulationQuery(");
        Source("Planning", "GradePlansHandler.cs").ShouldNotContain("RunSimulationQuery(");
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Paper", "CapturePaperFillsHandler.cs"))!, "*.cs"))
        {
            File.ReadAllText(file).ShouldNotContain("new RunSimulationQuery(", customMessage: file);
        }
    }

    [Fact]
    public void The_capture_code_never_moves_a_ticket_or_a_plan()
    {
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Paper", "CapturePaperFillsHandler.cs"))!, "*.cs"))
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("MoveItemAsync", customMessage: file);
            text.ShouldNotContain("MovePlanAsync", customMessage: file);
            text.ShouldNotContain("PublishAsync", customMessage: file);
        }
    }

    [Fact]
    public void The_paper_store_port_reads_and_inserts_and_never_updates_or_deletes()
    {
        typeof(IPaperStore).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Order()
            .ShouldBe(["CoverageAsync", "CurrentEpochAsync", "EnsureFirstEpochAsync", "ListEntriesAsync", "ListRunsAsync", "PairsAsync", "WriteSessionAsync"]);
    }
}
