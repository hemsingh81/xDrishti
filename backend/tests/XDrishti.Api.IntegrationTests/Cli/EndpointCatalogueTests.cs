using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using XDrishti.Api.Platform;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Reporting;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>Spec P8-17 criterion 5 through the real catalogue: a planted POST, DELETE or method-less route under a guarded prefix reaches the phase check as NOT MET; GET routes and other prefixes do not.</summary>
public sealed class EndpointCatalogueTests
{
    private static RouteEndpoint Route(string pattern, params string[] methods) => new(
        _ => Task.CompletedTask,
        RoutePatternFactory.Parse(pattern),
        0,
        methods.Length == 0 ? EndpointMetadataCollection.Empty : new EndpointMetadataCollection(new HttpMethodMetadata(methods)),
        pattern);

    private static PhaseVerdict FiveFor(params RouteEndpoint[] routes)
    {
        var routesSeen = new EndpointCatalogue(new DefaultEndpointDataSource(routes)).Routes();
        var facts = new ReportsPhaseFacts(1, 1, 0, ReportsPhaseAnswer.NoRuns, [], [], 0, [], true, true, true, routesSeen, null, null, [], null, null);
        return ReportsPhaseCheck.Evaluate(facts).Criteria.Single(c => c.Number == 5).Verdict;
    }

    [Fact]
    public void Get_routes_are_met_and_other_prefixes_are_ignored()
    {
        FiveFor(Route("/api/reports/runs", "GET"), Route("/api/portfolio/trades", "POST")).ShouldBe(PhaseVerdict.Met);
    }

    [Fact]
    public void A_planted_post_delete_or_any_method_route_is_not_met()
    {
        FiveFor(Route("/api/reports/runs", "GET"), Route("/api/reports/export", "POST")).ShouldBe(PhaseVerdict.NotMet);
        FiveFor(Route("/api/reports/runs", "GET"), Route("/api/lessons/{id}", "DELETE")).ShouldBe(PhaseVerdict.NotMet);
        FiveFor(Route("/api/reports/runs", "GET"), Route("/api/exports/x")).ShouldBe(PhaseVerdict.NotMet);
    }
}
