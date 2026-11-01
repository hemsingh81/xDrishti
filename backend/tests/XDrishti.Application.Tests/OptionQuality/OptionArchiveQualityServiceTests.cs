using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.OptionQuality;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.OptionQuality;

public sealed class OptionArchiveQualityServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void It_is_on_by_default_at_16_15_on_trading_days_with_one_retry_and_a_same_day_catch_up()
    {
        var settings = OptionArchiveQualityService.Definition.Defaults;

        OptionArchiveQualityService.Definition.Id.ShouldBe("options-quality");
        settings.Enabled.ShouldBeTrue();
        settings.Schedule.ShouldBe(Schedule.Daily(new TimeOnly(16, 15)));
        settings.Schedule.TradingDaysOnly.ShouldBeTrue();
        settings.Retries.ShouldBe(1);
        settings.CatchUp.ShouldBeTrue();
        OptionArchiveQualityService.Definition.Constraints.CatchUpReach.ShouldBe(CatchUpReach.SameDay);
        OptionArchiveQualityService.Definition.RunsAs.ShouldBe("xd-api");
    }

    [Theory]
    [InlineData(ServiceRunTrigger.Manual, "manual")]
    [InlineData(ServiceRunTrigger.Scheduled, "daily")]
    public async Task A_run_carries_out_the_checks_with_the_trigger_of_the_run(ServiceRunTrigger trigger, string stored)
    {
        var world = new QualityWorld();
        var runner = world.Runner();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(OptionArchiveQualityRunner)).Returns(runner);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);
        var service = new OptionArchiveQualityService(scopes);
        var context = new ServiceRunContext(Guid.NewGuid(), trigger, 1, "system", new Dictionary<string, System.Text.Json.JsonElement>(), (_, _) => ValueTask.CompletedTask);

        var outcome = await service.RunAsync(context, Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Succeeded);
        world.Store.Finished.ShouldHaveSingleItem().Trigger.ShouldBe(stored);
    }
}
