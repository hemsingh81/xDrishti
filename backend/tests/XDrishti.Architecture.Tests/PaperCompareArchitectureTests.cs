using System.Reflection;
using XDrishti.Application.Abstractions.Paper;

namespace XDrishti.Architecture.Tests;

/// <summary>Spec P10-04 (ADR 0122): the backtest-versus-paper report only reads. Its data port has one read member, its infrastructure issues only select statements, and its handler and endpoint name no write store and no ticket move.</summary>
public sealed class PaperCompareArchitectureTests
{
    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. parts]));

    [Fact]
    public void The_comparison_ports_only_read()
    {
        typeof(IBacktestPaperData).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ShouldBe(["ReadAsync"]);
        typeof(ICostConfirmationFact).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ShouldBe(["ConfirmedOnAsync"]);
    }

    [Fact]
    public void The_comparison_infrastructure_issues_only_select_statements()
    {
        var text = Source("XDrishti.Infrastructure", "Paper", "BacktestPaperData.cs");

        foreach (var verb in new[] { "INSERT ", "UPDATE ", "DELETE ", "TRUNCATE ", "DROP " })
        {
            text.ShouldNotContain(verb);
        }
    }

    [Fact]
    public void The_comparison_handler_names_no_write_store_no_ticket_move_and_no_confirmation_source()
    {
        foreach (var text in new[] { Source("XDrishti.Application", "Paper", "GetBacktestVsPaperHandler.cs"), Source("XDrishti.Application", "Paper", "BacktestReferenceReader.cs"), Source("XDrishti.Api", "Endpoints", "PaperCompareEndpoints.cs") })
        {
            text.ShouldNotContain("IPaperStore");
            text.ShouldNotContain("IGradeStore");
            text.ShouldNotContain("MoveItemAsync");
            text.ShouldNotContain("StoreAsync");
            text.ShouldNotContain("IChargeSheetConfirmationSource");
        }
    }
}
