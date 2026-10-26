using System.Reflection;
using XDrishti.Application.Abstractions.Paper;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P10-02 (ADR 0118): the paper ledger reads and derives and nothing else. No paper type reads or can set the charge-sheet confirmation (ADR 0100 keeps that to the report loader and the runs list), the ledger ports only read, and no paper read type
/// carries a write or a ticket move. The no-order tripwire covers the new types as well.
/// </summary>
public sealed class PaperLedgerArchitectureTests
{
    private static IEnumerable<string> PaperFiles()
    {
        var application = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Paper", "PaperLog.cs"))!;
        var domain = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "Paper", "PaperWording.cs"))!;
        var infrastructure = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Paper", "PaperLedgerData.cs"))!;
        return new[] { application, domain, infrastructure }.SelectMany(d => Directory.GetFiles(d, "*.cs"));
    }

    [Fact]
    public void No_paper_code_reads_or_sets_the_charge_sheet_confirmation()
    {
        foreach (var file in PaperFiles())
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("IChargeSheetConfirmationSource", customMessage: file);
            text.ShouldNotContain("ChargeSheetConfirmation", customMessage: file);
        }
    }

    [Fact]
    public void The_ledger_ports_only_read()
    {
        typeof(IPaperLedgerData).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Order()
            .ShouldBe(["ClosesAsync", "EntryAccountsAsync", "EpochAsync", "FinishedSessionsAsync", "TotalsAsync", "TradesAsync"]);
        typeof(IPaperBookReader).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ShouldBe(["ReadAsync"]);
    }

    [Fact]
    public void The_ledger_infrastructure_issues_only_select_statements()
    {
        var text = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Paper", "PaperLedgerData.cs"));

        foreach (var verb in new[] { "INSERT ", "UPDATE ", "DELETE ", "TRUNCATE ", "DROP " })
        {
            text.ShouldNotContain(verb);
        }
    }

    [Fact]
    public void The_ledger_handlers_name_no_ticket_move_and_no_store_write()
    {
        var application = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Paper", "PaperLedgerHandlers.cs"))!;
        foreach (var name in new[] { "PaperLedgerHandlers.cs", "PaperLedgerService.cs", "PaperBookReader.cs" })
        {
            var text = File.ReadAllText(Path.Combine(application, name));
            text.ShouldNotContain("IPaperStore", customMessage: name);
            text.ShouldNotContain("MoveItemAsync", customMessage: name);
            text.ShouldNotContain("WriteSessionAsync", customMessage: name);
        }
    }
}
