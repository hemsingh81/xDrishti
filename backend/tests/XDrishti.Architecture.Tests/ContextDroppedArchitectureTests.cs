using System.Reflection;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// ADR 0291: the NIFTY 50 / INDIA VIX market context (P3-01c context part, P3-01d, P3-lift, the CSV inbox of ADR 0290) is dropped. No assembly names one of its types, and the shared breadth kernel of the baskets is all that is left in the
/// Domain <c>Market.Context</c> namespace.
/// </summary>
public sealed class ContextDroppedArchitectureTests
{
    private static readonly Assembly[] Assemblies =
    [
        typeof(XDrishti.Domain.Common.Result).Assembly,
        typeof(XDrishti.Application.DependencyInjection).Assembly,
        typeof(XDrishti.Infrastructure.DependencyInjection).Assembly,
        typeof(XDrishti.Indicators.IndicatorCatalog).Assembly,
        typeof(XDrishti.Simulation.Running.Simulator).Assembly,
        typeof(XDrishti.Strategies.Compilation.StrategyCompiler).Assembly,
    ];

    private static readonly string[] RemovedTypeNames =
    [
        "MarketContext", "ContextDay", "ContextValues", "ContextOptions", "ContextDayRecord", "ContextFigures", "ContextRead", "ContextBasis", "ContextCoverage", "ContextHealth", "ContextDerivation", "ContextFingerprint",
        "IContextFigures", "IContextDayStore", "IContextSettings", "IContextBarSource", "IContextInbox", "IContextUpkeep", "RegimeLabel", "RegimeNames", "RegimeBreakdown", "RegimeCellBuilder", "StoredBreadth", "ContextDailyRecord",
        "ContextVixCsvRecord", "RefreshContextHandler", "LoadContextInboxHandler", "GetContextCoverageHandler", "GetRegimeLabelsHandler", "ComputeBreadthHandler", "RebuildBreadthHandler",
    ];

    [Fact]
    public void No_assembly_has_a_type_of_the_market_context()
    {
        var found = Assemblies.SelectMany(a => a.GetTypes()).Where(t => RemovedTypeNames.Contains(t.Name, StringComparer.Ordinal)).Select(t => t.FullName).ToList();

        found.ShouldBeEmpty();
    }

    [Fact]
    public void The_domain_market_context_namespace_holds_only_the_breadth_kernel_the_baskets_share()
    {
        var domain = Assemblies[0];

        var names = domain.GetTypes().Where(t => t.Namespace == "XDrishti.Domain.Market.Context" && !t.Name.StartsWith('<')).Select(t => t.Name).Order(StringComparer.Ordinal).ToList();

        names.ShouldBe(["BreadthCalculator", "BreadthInput", "BreadthOptions", "BreadthResult"]);
    }

    [Fact]
    public void The_date_features_of_the_indicator_library_are_the_calendar_results_and_relative_strength_ones()
    {
        var kinds = Enum.GetNames<XDrishti.Indicators.DateFeatureKind>().Order(StringComparer.Ordinal).ToList();

        kinds.ShouldBe(["DaysSinceResults", "DaysToResults", "ExpiryDay", "MomRank", "ResultsCar3Quintile", "ResultsSueQuintile", "RsVsIndex", "SectorRs", "Weekday"]);
    }

    [Fact]
    public void The_production_code_names_no_table_of_the_market_context()
    {
        var root = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "XDrishti.Application.csproj"))!;
        var src = Path.GetDirectoryName(root)!;
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/Migrations/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("/bin/", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("market.context_daily", StringComparison.Ordinal) || File.ReadAllText(f).Contains("market.context_vix_csv", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(src, f))
            .ToList();

        offenders.ShouldBeEmpty();
    }
}
