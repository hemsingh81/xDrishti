using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Which plan builder the nightly gets (P7-04, ADR 0088): the null builder unless the setting turns the selection builder on.</summary>
public sealed class PlanBuilderChoiceTests
{
    private static IPlanBuilder Resolve(bool? flag)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddScoped<ITicketSource, NoTickets>();
        services.AddScoped<IPlanContextSource, NoContext>();
        if (flag is { } value)
        {
            services.AddSingleton<IPlanBuilderSettings>(new Settings(value));
        }

        var provider = services.BuildServiceProvider();
        return provider.CreateScope().ServiceProvider.GetRequiredService<IPlanBuilder>();
    }

    [Fact]
    public void The_null_builder_is_the_default_without_a_setting() => Assert.IsType<NullPlanBuilder>(Resolve(null));

    [Fact]
    public void The_null_builder_is_used_when_the_flag_is_off() => Assert.IsType<NullPlanBuilder>(Resolve(false));

    [Fact]
    public void The_selection_builder_is_used_only_when_the_flag_is_on() => Assert.IsType<SelectionPlanBuilder>(Resolve(true));

    private sealed class NoTickets : ITicketSource
    {
        public ValueTask<Result<TicketSourceResult>> ReadAsync(DateOnly session, DateOnly forDate, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result.Success(new TicketSourceResult([], 0, 0, [])));
    }

    private sealed class NoContext : IPlanContextSource
    {
        public ValueTask<Result<PlanContext>> ReadAsync(PlanContextRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result.Success(new PlanContext([], new Dictionary<string, InstrumentFacts>(), [], null, null, null, [])));
    }

    private sealed class Settings(bool useSelection) : IPlanBuilderSettings
    {
        public bool UseSelection => useSelection;
    }
}
