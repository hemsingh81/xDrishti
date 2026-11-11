using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Options;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P14-01 criterion 9 (ADR 0175): the option reference data has no trading use yet. The decision code of the Domain, the pure libraries and the planning,
/// learning and portfolio code reference no option type; the master list's scope does not know options; the store has no method that reads a contract; no API
/// route and no other module names the option tables.
/// </summary>
public sealed class OptionArchiveArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly string[] OptionNamespaces =
        ["XDrishti.Domain.Options", "XDrishti.Application.Options", "XDrishti.Application.Abstractions.Options", "XDrishti.Infrastructure.OptionContracts"];

    private static string Root => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    [Theory]
    [InlineData("XDrishti.Application.Planning")]
    [InlineData("XDrishti.Application.Learning")]
    [InlineData("XDrishti.Application.Portfolio")]
    [InlineData("XDrishti.Application.Research")]
    [InlineData("XDrishti.Application.Lab")]
    [InlineData("XDrishti.Application.Paper")]
    [InlineData("XDrishti.Application.LiveTrading")]
    [InlineData("XDrishti.Application.Live")]
    public void The_decision_code_references_no_option_type(string area)
    {
        var result = Types.InAssembly(Application).That().ResideInNamespaceStartingWith(area).ShouldNot().HaveDependencyOnAny(OptionNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_domain_outside_its_options_folder_and_the_pure_libraries_reference_no_option_type()
    {
        var domain = Types.InAssembly(Domain).That().DoNotResideInNamespaceStartingWith("XDrishti.Domain.Options").ShouldNot().HaveDependencyOnAny(OptionNamespaces).GetResult();
        domain.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", domain.FailingTypeNames ?? []));
        foreach (var name in new[] { "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Indicators", "XDrishti.SimExport", "XDrishti.SimReader" })
        {
            Types.InAssembly(Assembly.Load(new AssemblyName(name))).ShouldNot().HaveDependencyOnAny(OptionNamespaces).GetResult().IsSuccessful.ShouldBeTrue(name);
        }
    }

    [Fact]
    public void Only_the_refresh_step_and_the_master_parser_use_the_option_code_in_application()
    {
        var result = Types.InAssembly(Application).That().DoNotResideInNamespaceStartingWith("XDrishti.Application.Options")
            .And().DoNotResideInNamespace("XDrishti.Application.Abstractions.Options")
            .And().DoNotResideInNamespace("XDrishti.Application.Market.Import")
            .And().DoNotResideInNamespace("XDrishti.Application.Market.Refresh")
            .And().DoNotResideInNamespace("XDrishti.Application")
            .ShouldNot().HaveDependencyOnAny(OptionNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_store_has_no_method_that_reads_a_contract()
    {
        typeof(IOptionContractStore).GetMethods().Select(m => m.Name).Order().ShouldBe(["ApplyAsync", "ResolveUnderlyingsAsync"]);
        typeof(IOptionUnderlyingSetting).GetMethods().Select(m => m.Name).Order().ShouldBe(["FixDefaultAsync", "MostTradedFnoSharesAsync", "ReadAsync"]);
    }

    [Fact]
    public void The_master_list_scope_does_not_know_options()
    {
        var text = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "Market", "InstrumentScope.cs"));
        text.ShouldNotContain("OPTIDX", Case.Sensitive);
        text.ShouldNotContain("OPTSTK", Case.Sensitive);
        text.ShouldNotContain("OptionScope", Case.Sensitive);
        text.ShouldNotContain("XDrishti.Domain.Options", Case.Sensitive);
    }

    [Fact]
    public void Only_the_option_store_and_the_quality_reference_read_the_option_tables_in_sql_and_no_api_route_reaches_them()
    {
        var named = Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), "(FROM|INTO|UPDATE|JOIN)\\s+options\\.(contracts|lot_sizes|market_epochs)", System.Text.RegularExpressions.RegexOptions.None, ScanRegex.Timeout))
            .Select(Path.GetFileName).Order().ToList();

        // P14-04 (ADR 0178) added exactly one reader of these reference tables, the quality checks' OptionReference (strike step, lot history, rule epochs); P14-08 (ADR 0197) added exactly one more, the re-mark's ContractExpiryReader (the expiry dates of one underlying in a date range); BK-05b (ADR 0256) added the NseLotApplier (an insert of NSE lot rows that reads the newest row of its own source). A further file here is a failure.
        named.ShouldBe(["ContractExpiryReader.cs", "NseLotApplier.cs", "OptionContractStore.cs", "OptionReference.cs"]);
        Directory.GetFiles(Path.Combine(Root, "src", "XDrishti.Api"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && File.ReadAllText(f).Contains("XDrishti.Application.Abstractions.Options", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void No_code_updates_or_deletes_a_row_of_the_lot_table()
    {
        var offenders = Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), "(UPDATE|DELETE\\s+FROM|TRUNCATE(\\s+TABLE)?)\\s+options\\.lot_sizes", System.Text.RegularExpressions.RegexOptions.IgnoreCase, ScanRegex.Timeout))
            .Select(Path.GetFileName).ToList();

        offenders.ShouldBeEmpty("options.lot_sizes is insert-only (BK-05b): a lot row is never updated or deleted");
    }
}
