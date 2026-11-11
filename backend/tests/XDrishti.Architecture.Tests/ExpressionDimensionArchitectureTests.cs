using System.Text.RegularExpressions;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-06 criterion 6 (ADR 0193): the instrument expression is research only. A value of <c>Expression</c> other than <c>Cash</c> is named only in Domain/Learning, Domain/Options, Domain/Pairs, the learning and research stores and tests; the expression types
/// are named by no selection, planning, paper, live or execution code (the type, its names and the `.Expression` member alike; they use `IsCash`), nor by the execution service; every reader of stored cell rows is decided here, and each decision path filters to cash.
/// </summary>
public sealed partial class ExpressionDimensionArchitectureTests
{
    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Src => Path.Combine(Root, "src");

    private static List<string> Files(params string[] under) =>
    [
        .. under.SelectMany(u => Directory.EnumerateFiles(Path.Combine(Src, u), "*.cs", SearchOption.AllDirectories)).Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)),
    ];

    private static string Rel(string file) => Path.GetRelativePath(Src, file).Replace('\\', '/');

    [GeneratedRegex(@"\bExpression\.(Future|OptionBuy|DebitSpread|CreditSpread|IronCondor|Pair|Basis)\b")]
    private static partial Regex NonCash();

    [GeneratedRegex(@"\bExpression(Profile|Names)?\b|\bIExpressionProfileStore\b|\bStoredExpressionProfile\b")]
    private static partial Regex ExpressionTypes();

    private static readonly string[] AllowedForNonCash = ["XDrishti.Domain/Learning/", "XDrishti.Domain/Options/", "XDrishti.Domain/Pairs/", "XDrishti.Application/OptionExpression/", "XDrishti.Infrastructure/Learning/", "XDrishti.Infrastructure/Research/"];

    [Fact]
    public void A_non_cash_expression_value_is_named_only_in_the_research_areas()
    {
        var offenders = Files(".").Where(f => NonCash().IsMatch(File.ReadAllText(f))).Select(Rel).Where(f => !AllowedForNonCash.Any(a => f.StartsWith(a, StringComparison.Ordinal))).Order(StringComparer.Ordinal).ToList();

        offenders.ShouldBeEmpty("A non-cash expression value outside the research areas: " + string.Join(", ", offenders));
        Files("XDrishti.Domain").Count(f => NonCash().IsMatch(File.ReadAllText(f))).ShouldBeGreaterThan(0, "the rule is not vacuous: the Domain names them");
    }

    [Fact]
    public void No_decision_or_execution_code_names_the_expression_types()
    {
        string[] decision =
        [
            "XDrishti.Execution", "XDrishti.Domain/Selection", "XDrishti.Domain/Planning", "XDrishti.Domain/Paper", "XDrishti.Domain/LiveTrading", "XDrishti.Domain/Execution",
            "XDrishti.Application/Selection", "XDrishti.Application/Planning", "XDrishti.Application/Paper", "XDrishti.Application/LiveTrading", "XDrishti.Application/Execution", "XDrishti.Infrastructure/Planning", "XDrishti.Infrastructure/Paper", "XDrishti.Infrastructure/Execution",
        ];
        var offenders = decision.Where(d => Directory.Exists(Path.Combine(Src, d))).SelectMany(d => Files(d)).Where(f => ExpressionTypes().IsMatch(File.ReadAllText(f))).Select(Rel).Order(StringComparer.Ordinal).ToList();

        offenders.ShouldBeEmpty("Decision or execution code names an expression type: " + string.Join(", ", offenders));
        Directory.Exists(Path.Combine(Src, "XDrishti.Execution")).ShouldBeTrue();
        ExpressionTypes().IsMatch("cell.Expression").ShouldBeTrue("the scan sees the member and the type");
    }

    [Fact]
    public void Every_reader_of_stored_cell_rows_is_decided_and_each_decision_path_keeps_to_cash_cells()
    {
        // Display and statistics readers (they list every cell, with its expression) and the paths that take cash cells only.
        string[] display =
        [
            "XDrishti.Application/Learning/GetCellHandler.cs", "XDrishti.Application/Learning/GetLearningOverviewHandler.cs", "XDrishti.Application/Learning/ListCellsHandler.cs", "XDrishti.Application/Learning/ListRegimeCellsQuery.cs", "XDrishti.Application/Learning/UniverseCellsQueries.cs",
            "XDrishti.Application/Ai/Proposals/ProposalFacts.cs", "XDrishti.Application/Paper/BacktestReferenceReader.cs", "XDrishti.Infrastructure/Learning/CellStatsStore.cs", "XDrishti.Application/Abstractions/Learning/ICellStatsStore.cs",
        ];
        string[] cashOnly =
        [
            "XDrishti.Application/Selection/ShowSelectionHandler.cs", "XDrishti.Infrastructure/Planning/StoredTicketSource.cs", "XDrishti.Application/Paper/ConfigSnapshotSource.cs",
        ];
        var readers = Files(".").Where(f => Regex.IsMatch(File.ReadAllText(f), @"\.(ListLatestAsync|ListLatestCashAsync|ListLatestBeforeAsync|ListRegimeLatestAsync|ListUniverseLatestAsync|FindLatestAsync)\(")).Select(Rel)
            .Where(r => !r.StartsWith("XDrishti.Infrastructure/Auth/", StringComparison.Ordinal) && !r.StartsWith("XDrishti.Application/Auth/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToList();

        var undecided = readers.Where(r => !display.Contains(r) && !cashOnly.Contains(r)).ToList();

        undecided.ShouldBeEmpty("A new reader of cell rows must be decided here (display, or cash only): " + string.Join(", ", undecided));
        foreach (var path in cashOnly)
        {
            File.ReadAllText(Path.Combine(Src, path)).ShouldContain("CashCells.Only(", Case.Sensitive, path);
        }

        File.ReadAllText(Path.Combine(Src, "XDrishti.Application/Planning/TicketReplayer.cs")).ShouldContain("cell.IsCash");
        File.ReadAllText(Path.Combine(Src, "XDrishti.Application/Paper/CapturePaperFillsHandler.cs")).ShouldContain("IsCash: false");
        File.ReadAllText(Path.Combine(Src, "XDrishti.Application/Paper/ConfigSnapshotSource.cs")).ShouldContain("IsCash: true");
    }
}
