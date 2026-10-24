#pragma warning disable CA2012 // NSubstitute setup and verification calls on ValueTask members are not awaited
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Planning;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Planning;

/// <summary>The suggestions handler and service (spec P7-09).</summary>
public sealed class SuggestionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Monday = new(2026, 10, 26);

    private static PlanRow Row(string basis) => new(7, new NewPlan("k", Monday, DateTimeOffset.UnixEpoch, 1, new string('a', 64), basis, true, "t", DateTimeOffset.UnixEpoch, []), PlanState.Published, 0, 0, 1);

    private static PlanItemRow Rejected() => new(1, 7, new NewPlanItem(1, TicketGroup.Rejected, "AAA", "BUY", "S", "h", "t", "s", "g", "v", ["cost:unavailable"], new PlanEvidence("c", 40, "d", "b", 0.5, 0.3, [], null), []), TicketState.Proposed);

    private static (GenerateSuggestionsHandler Handler, ISuggestionStore Store) Handler(PlanRow? plan, DateOnly? statsAsOf)
    {
        var plans = Substitute.For<IPlanStore>();
        plans.ListAsync(Arg.Any<DateOnly?>(), Arg.Any<Guid?>(), 1, Ct).Returns(ValueTask.FromResult<IReadOnlyList<PlanRow>>(plan is null ? [] : [plan]));
        plans.ItemsAsync(7, Ct).Returns(ValueTask.FromResult<IReadOnlyList<PlanItemRow>>([Rejected()]));
        var store = Substitute.For<ISuggestionStore>();
        store.InsertAsync(Arg.Any<long>(), Arg.Any<IReadOnlyList<NewSuggestion>>(), Arg.Any<DateTimeOffset>(), Ct).Returns(call => ValueTask.FromResult(call.Arg<IReadOnlyList<NewSuggestion>>().Count));
        var stats = Substitute.For<ICellStatsStore>();
        stats.NewestAsOfAsync(Ct).Returns(ValueTask.FromResult(statsAsOf));
        return (new GenerateSuggestionsHandler(plans, store, stats, new FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero)), NullLogger<GenerateSuggestionsHandler>.Instance), store);
    }

    [Fact]
    public async Task It_stores_the_suggestions_the_rules_produce_for_the_newest_plan()
    {
        var (handler, store) = Handler(Row("Proposed. market gate unavailable (vix)"), new DateOnly(2026, 10, 22));

        var result = await handler.HandleAsync(new GenerateSuggestionsCommand(null), Ct);

        result.Value.PlanId.ShouldBe(7);
        result.Value.Suggestions.Select(s => s.Kind).ShouldBe([SuggestionKinds.MarketGateUnavailable, SuggestionKinds.NoPrimaryTickets, SuggestionKinds.TicketsHeldUnavailableInput]);
        await store.Received(1).InsertAsync(7, Arg.Is<IReadOnlyList<NewSuggestion>>(l => l.Count == 3), new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero), Ct);
    }

    [Fact]
    public async Task With_no_plan_it_writes_nothing()
    {
        var (handler, store) = Handler(null, null);

        var result = await handler.HandleAsync(new GenerateSuggestionsCommand(null), Ct);

        result.Value.PlanId.ShouldBeNull();
        await store.DidNotReceiveWithAnyArgs().InsertAsync(default, default!, default, Ct);
    }

    [Fact]
    public void The_service_follows_the_nightly_plan_and_starts_paused_with_no_schedule_issue()
    {
        var d = SuggestionsService.Definition;

        d.Id.ShouldBe("suggestions");
        d.Defaults.Enabled.ShouldBeFalse();
        d.Defaults.Schedule.ShouldBe(Schedule.AfterService("nightly"));
        var subjects = new[] { EodService.Definition, PortfolioBrokerHistoryService.Definition, NightlyService.Definition, d }.Select(x => new RuleSubject(x, x.Defaults)).ToList();
        ScheduleRules.Evaluate(subjects).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_service_run_reports_the_counts()
    {
        var handler = Substitute.For<ICommandHandler<GenerateSuggestionsCommand, SuggestionsOutcome>>();
        handler.HandleAsync(Arg.Any<GenerateSuggestionsCommand>(), Ct).Returns(Result.Success(new SuggestionsOutcome(7, Monday, 3, 2, [])));
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<GenerateSuggestionsCommand, SuggestionsOutcome>)).Returns(handler);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        var context = new ServiceRunContext(Guid.NewGuid(), ServiceRunTrigger.Scheduled, 1, "system", new Dictionary<string, JsonElement>(), (_, _) => ValueTask.CompletedTask);

        var outcome = await new SuggestionsService(factory).RunAsync(context, Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Succeeded);
        outcome.Message.ShouldNotBeNull().ShouldContain("3 suggestion(s), 2 new");
    }
}
