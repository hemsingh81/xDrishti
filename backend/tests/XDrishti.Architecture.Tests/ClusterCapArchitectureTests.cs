using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Domain.Selection;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// BK-08 criterion 13 (ADR 0260): the cluster cap, the gate's breadth source and the basket context place no order. The pure kernels reference no I/O, network, database, clock or logging type; no file of the slice names an order, broker, execution, margin or leverage member;
/// and the three settings keys are exactly the stored ones, with the defaults the old behaviour (tracked set, no cap) and no execution word in a key.
/// </summary>
public sealed partial class ClusterCapArchitectureTests
{
    private static readonly string[] Files =
    [
        Path.Combine("XDrishti.Domain", "Selection", "ClusterOptions.cs"),
        Path.Combine("XDrishti.Domain", "Selection", "CorrelationCluster.cs"),
        Path.Combine("XDrishti.Domain", "Selection", "ClusterResult.cs"),
        Path.Combine("XDrishti.Domain", "Selection", "CorrelationClusters.cs"),
        Path.Combine("XDrishti.Domain", "Selection", "BasketSettings.cs"),
        Path.Combine("XDrishti.Domain", "Selection", "BasketSettingChange.cs"),
        Path.Combine("XDrishti.Domain", "Selection", "BasketSettingChanges.cs"),
        Path.Combine("XDrishti.Domain", "Selection", "SmartRules", "GateBreadthSource.cs"),
        Path.Combine("XDrishti.Domain", "Reporting", "BasketAnalytics", "BasketContextRow.cs"),
        Path.Combine("XDrishti.Domain", "Reporting", "BasketAnalytics", "BasketContextCalculator.cs"),
        Path.Combine("XDrishti.Application", "Abstractions", "Selection", "IBasketSettingsStore.cs"),
        Path.Combine("XDrishti.Application", "Abstractions", "Market", "Context", "IBasketContextStore.cs"),
        Path.Combine("XDrishti.Application", "Selection", "BasketSettingsModels.cs"),
        Path.Combine("XDrishti.Application", "Selection", "BasketSettingsAudit.cs"),
        Path.Combine("XDrishti.Application", "Selection", "ChangeBasketSettingsHandler.cs"),
        Path.Combine("XDrishti.Application", "Selection", "GetBasketSettingsHandler.cs"),
        Path.Combine("XDrishti.Application", "Market", "Context", "ComputeBasketContext.cs"),
        Path.Combine("XDrishti.Application", "Market", "Context", "GetBasketContext.cs"),
        Path.Combine("XDrishti.Infrastructure", "Selection", "BasketSettingsStore.cs"),
        Path.Combine("XDrishti.Infrastructure", "Market", "BasketContextStore.cs"),
        Path.Combine("XDrishti.Api", "Endpoints", "BasketSettingsEndpoints.cs"),
    ];

    [GeneratedRegex(@"PlaceOrder|IOrder\w*|\bBroker\w*|\bExecution\w*|IExec\w*|\bMargin\w*|\bLeverag\w*|\bUpsiz\w*", RegexOptions.CultureInvariant)]
    private static partial Regex Banned();

    [Fact]
    public void The_pure_kernels_reference_no_io_network_database_clock_or_logging_type()
    {
        var result = Types.InAssembly(typeof(CorrelationClusters).Assembly)
            .That().HaveNameStartingWith("CorrelationCluster").Or().HaveName("ClusterOptions").Or().HaveName("ClusterResult").Or().HaveName("BasketSettings").Or().HaveNameStartingWith("BasketSettingChange").Or().HaveName("GateBreadthSource").Or().HaveName("BasketContextCalculator")
            .ShouldNot().HaveDependencyOnAny("System.IO", "System.Net", "System.Data", "Npgsql", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "System.Threading.Tasks", "System.Diagnostics")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void No_file_of_the_slice_names_an_order_broker_execution_margin_or_leverage_member()
    {
        var src = Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj")))!;
        foreach (var file in Files)
        {
            var path = Path.Combine(src, file);
            File.Exists(path).ShouldBeTrue(path);
            Banned().IsMatch(File.ReadAllText(path)).ShouldBeFalse(file);
        }
    }

    [Fact]
    public void The_three_settings_keys_are_exactly_the_stored_ones_and_the_defaults_are_the_old_behaviour()
    {
        var keys = typeof(BasketSettings).GetFields().Where(f => f.IsLiteral && f.Name.EndsWith("Key", StringComparison.Ordinal)).Select(f => (string)f.GetRawConstantValue()!).Order().ToList();

        keys.ShouldBe(["gate.breadth_source", "selection.cluster_rho", "selection.max_cluster_risk_pct"]);
        keys.ShouldNotContain(k => Banned().IsMatch(k) || k.Contains("order", StringComparison.OrdinalIgnoreCase) || k.Contains("exec", StringComparison.OrdinalIgnoreCase));
        BasketSettings.Defaults.BreadthSource.ShouldBe("tracked");
        BasketSettings.Defaults.MaxClusterRiskPct.ShouldBeNull();
        new FillConstraints().MaxClusterRiskPct.ShouldBeNull();
    }
}
