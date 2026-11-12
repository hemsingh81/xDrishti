using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Lists;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Market.Lists;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>DATA-01 wiring: the real host composes the list filter, its ports and both owner use cases, with the switches off by default.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ListFilterCompositionTests(ApiFactory factory)
{
    [Fact]
    public void The_real_host_resolves_the_list_filter_the_selection_builder_the_ports_and_the_verbs_handlers()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var application = typeof(IPlanBuilder).Assembly;

        sp.GetRequiredService(application.GetType("XDrishti.Application.Planning.PlanListFilter")!).ShouldNotBeNull();
        sp.GetRequiredService(application.GetType("XDrishti.Application.Planning.SelectionPlanBuilder")!).ShouldNotBeNull();
        sp.GetRequiredService<IListFlagsAsOf>().ShouldNotBeNull();
        sp.GetRequiredService<IListArchiveSource>().ShouldNotBeNull();
        sp.GetRequiredService<IListDayStore>().ShouldNotBeNull();
        sp.GetRequiredService<ICommandHandler<ListsProbeCommand, ListsProbeReport>>().ShouldNotBeNull();
        sp.GetRequiredService<ICommandHandler<ListsBackfillCommand, ListsBackfillSummary>>().ShouldNotBeNull();
        sp.GetRequiredService<IListFilterSettings>().Enabled.ShouldBeFalse();
        sp.GetRequiredService<IListHistorySettings>().Enabled.ShouldBeFalse();
    }
}
