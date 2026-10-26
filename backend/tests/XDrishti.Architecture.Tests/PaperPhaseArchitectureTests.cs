using System.Reflection;
using XDrishti.Application.Abstractions.Paper;

namespace XDrishti.Architecture.Tests;

/// <summary>Spec P10-06 (ADR 0123): the paper phase check only reads. Its data port has one read member, its infrastructure issues only select statements, and its handler, verb, endpoint and review line name no write store, no ticket move and no setting change.</summary>
public sealed class PaperPhaseArchitectureTests
{
    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. parts]));

    [Fact]
    public void The_phase_port_only_reads()
    {
        typeof(IPaperPhaseData).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ShouldBe(["CaptureRunsAsync"]);
    }

    [Fact]
    public void The_phase_infrastructure_issues_only_select_statements()
    {
        var text = Source("XDrishti.Infrastructure", "Paper", "PaperPhaseData.cs");

        foreach (var verb in new[] { "INSERT ", "UPDATE ", "DELETE ", "TRUNCATE ", "DROP " })
        {
            text.ShouldNotContain(verb);
        }
    }

    [Fact]
    public void The_phase_handler_verb_endpoint_and_line_name_no_write_member()
    {
        var files = new[]
        {
            Source("XDrishti.Application", "Paper", "EvaluatePaperPhaseCheckHandler.cs"),
            Source("XDrishti.Application", "Paper", "BacktestReviewComparisonLine.cs"),
            Source("XDrishti.Api", "Endpoints", "PaperPhaseCheckEndpoints.cs"),
            Source("XDrishti.Api", "Cli", "PaperPhaseCheckCommand.cs"),
        };

        foreach (var text in files)
        {
            foreach (var name in new[] { "IPaperStore", "IGradeStore", "MoveItemAsync", "MovePlanAsync", "PublishAsync", "RecordRunAsync", "WriteSessionAsync", "CommitAsync", "StartOwnerEpochAsync", "AddNoteAsync", "SaveAsync", "IChargeSheetConfirmationSource" })
            {
                text.ShouldNotContain(name);
            }
        }
    }
}
