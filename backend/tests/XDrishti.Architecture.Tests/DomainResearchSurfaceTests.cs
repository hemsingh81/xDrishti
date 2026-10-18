namespace XDrishti.Architecture.Tests;

/// <summary>
/// What the research family adds to the Domain stays small and known (P3-02b, ADR 0036; P3-03a): the public types of <c>XDrishti.Domain.Research</c> are listed here, so that a type added to the Domain by a slice of the simulator is
/// a decision someone made and not a side effect. The family is shared by the strategy model (<c>Side</c>), the scanner (<c>ScanCounters</c>, <c>ConditionCounter</c>) and, once merged, the walker (<c>WalkCounters</c>, <c>SimTrade</c>
/// and their enums): each of those slices adds its names to this list in its own change. No other namespace of the Domain gains a type for them.
/// </summary>
public sealed class DomainResearchSurfaceTests
{
    private static readonly System.Reflection.Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static readonly string[] Namespaces =
    [
        "XDrishti.Domain.Accounts", "XDrishti.Domain.Auth", "XDrishti.Domain.Common", "XDrishti.Domain.Market", "XDrishti.Domain.Market.Actions", "XDrishti.Domain.Market.Bars", "XDrishti.Domain.Market.Baskets",
        "XDrishti.Domain.Market.Calendar", "XDrishti.Domain.Market.Quality", "XDrishti.Domain.Platform", "XDrishti.Domain.Platform.Services", "XDrishti.Domain.Portfolio", "XDrishti.Domain.Research",
    ];

    [Fact]
    public void The_public_types_of_the_research_namespace_are_exactly_the_known_ones()
    {
        var research = Domain.GetExportedTypes().Where(t => t.Namespace == "XDrishti.Domain.Research").Select(t => t.Name).Order().ToList();

        research.ShouldBe(["ConditionCounter", "ScanCounters", "Side"]);
    }

    [Fact]
    public void The_domain_has_no_namespace_beyond_the_known_ones_so_the_simulator_adds_nothing_elsewhere()
    {
        Domain.GetExportedTypes().Select(t => t.Namespace ?? string.Empty).Distinct().Except(Namespaces).ShouldBeEmpty();
    }

    [Fact]
    public void The_research_types_are_plain_values_that_depend_on_no_other_project()
    {
        var research = Domain.GetExportedTypes().Where(t => t.Namespace == "XDrishti.Domain.Research").ToList();

        research.ShouldAllBe(t => t.IsEnum || t.IsSealed);
        Domain.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain(n => n != null && n.StartsWith("XDrishti.", StringComparison.Ordinal));
    }
}
