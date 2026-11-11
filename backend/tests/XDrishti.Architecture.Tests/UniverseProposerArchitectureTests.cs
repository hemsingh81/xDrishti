using System.Text.RegularExpressions;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// BK-11b (ADR 0266): the tier proposer and the dated replay apply nothing. No file of the slice names an order, broker, execution, margin or leverage member; none of the proposer's files reaches a strategy store, a cell-state writer, the selection or the planning
/// code (a universe link is only ever PROPOSED as a hypothesis); the one place that writes the hypothesis is the existing proposing handler; and the auto option is a stored, OFF-by-default universe setting.
/// </summary>
public sealed partial class UniverseProposerArchitectureTests
{
    private static readonly string[] Files =
    [
        Path.Combine("XDrishti.Domain", "Research", "DatedMembers.cs"),
        Path.Combine("XDrishti.Domain", "Research", "UniverseBasisRecord.cs"),
        Path.Combine("XDrishti.Domain", "Learning", "UniverseLinkProposal.cs"),
        Path.Combine("XDrishti.Application", "Learning", "ProposeFromUniverseLinkHandler.cs"),
        Path.Combine("XDrishti.Application", "Learning", "AutoProposeUniverseLinksHandler.cs"),
    ];

    [GeneratedRegex(@"PlaceOrder|IOrder\w*|\bBroker\w*|\bExecution\w*|IExec\w*|\bMargin\w*|\bLeverag\w*|\bUpsiz\w*", RegexOptions.CultureInvariant)]
    private static partial Regex Banned();

    [GeneratedRegex(@"IStrategyStore|IStrategyWriter|ICellStatsStore\.(Store|Append|Write)|IPlanStore|ISelection\w*|Selection\.|Planning\.|IActivation\w*|ICellState\w*|ILifecycle\w*|ScanScope\w*Apply|ApplyScope|\.Apply\w*\(", RegexOptions.CultureInvariant)]
    private static partial Regex Applies();

    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj")))!, file));

    [Fact]
    public void No_file_of_the_slice_names_an_order_broker_execution_margin_or_leverage_member()
    {
        foreach (var file in Files)
        {
            Banned().IsMatch(Source(file)).ShouldBeFalse(file);
        }
    }

    [Fact]
    public void No_file_of_the_proposer_reaches_a_strategy_a_cell_state_the_selection_or_a_plan_so_nothing_is_applied()
    {
        foreach (var file in Files.Where(f => f.Contains("Learning", StringComparison.Ordinal)))
        {
            Applies().IsMatch(Source(file)).ShouldBeFalse(file);
        }
    }

    [Fact]
    public void The_proposer_writes_a_hypothesis_only_through_the_proposing_handler()
    {
        foreach (var file in Files.Where(f => f.Contains("Application", StringComparison.Ordinal)))
        {
            var text = Source(file);
            text.ShouldNotContain(".ProposeAsync(new NewHypothesisRow", Case.Sensitive, file);
            text.ShouldNotContain("AppendAsync", Case.Sensitive, file);
        }

        Source(Path.Combine("XDrishti.Application", "Learning", "ProposeFromUniverseLinkHandler.cs")).ShouldContain("ICommandHandler<ProposeHypothesisCommand, HypothesisMoveOutcome>");
    }

    [Fact]
    public void The_web_proposal_is_one_owner_act_through_the_one_flow_with_a_capability_a_password_and_no_actor_in_the_body()
    {
        var endpoint = Source(Path.Combine("XDrishti.Api", "Endpoints", "UniverseProposeEndpoints.cs"));

        Regex.Count(endpoint, @"MapPost\(", RegexOptions.CultureInvariant, ScanRegex.Timeout).ShouldBe(1);
        Regex.Count(endpoint, @"OwnerActFlow\.RunAsync\(", RegexOptions.CultureInvariant, ScanRegex.Timeout).ShouldBe(1);
        Regex.Count(endpoint, @"RequireRateLimiting\(", RegexOptions.CultureInvariant, ScanRegex.Timeout).ShouldBe(1);
        endpoint.ShouldContain("Cap = Domain.Capabilities.Capabilities.LearningMovesUi");
        typeof(Program).Assembly.GetType("XDrishti.Api.Endpoints.ProposeFromUniverseLinkRequest")!.GetProperties().Select(p => p.Name).Order().ShouldBe(["BaselineRunKey", "Password", "Reason", "Window"]);
    }

    [Fact]
    public void The_auto_option_is_a_stored_universe_setting_that_is_off_by_default_and_names_no_execution_word()
    {
        UniverseSettingsRules.AutoProposeKey.ShouldBe("learning.universe_link.auto_propose");
        UniverseSettings.Defaults.AutoPropose.ShouldBeFalse();
        Banned().IsMatch(UniverseSettingsRules.AutoProposeKey).ShouldBeFalse();
    }

    [Fact]
    public void A_scope_knob_and_the_universe_source_are_the_only_new_members_of_the_hypothesis_enums()
    {
        Enum.GetNames<ExperimentKnobKind>().ShouldBe(["Parameter", "Exit", "Slippage", "Scope"]);
        Enum.GetNames<HypothesisSource>().ShouldBe(["Owner", "Grid", "Assistant", "Universe"]);
    }

    [Fact]
    public void The_dated_basis_values_are_the_two_the_spec_names()
    {
        (UniverseBasisRecord.Dated, UniverseBasisRecord.SingleSnapshot).ShouldBe(("dated", "single_snapshot"));
    }
}
