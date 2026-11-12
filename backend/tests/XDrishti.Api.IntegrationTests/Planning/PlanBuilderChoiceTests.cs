using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Which plan builder the nightly gets (P7-04, ADR 0088): the null builder unless the setting turns the selection builder on.</summary>
public sealed class PlanBuilderChoiceTests
{
    private static IPlanBuilder Resolve(bool? flag, bool withAutoSwitch = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddScoped<ITicketSource, NoTickets>();
        services.AddScoped<IPlanContextSource, NoContext>();
        services.AddScoped<IEventFilterDecisions, NoDecisions>();   // the event-risk filter of the selection builder (P15-06) needs the switch history and the calendar behind it
        services.AddScoped<ICalendarRows, NoRows>();
        services.AddScoped<ITradingCalendar, PlainCalendar>();
        services.AddSingleton<IFilingSettings, NoSettings>();
        services.AddScoped<XDrishti.Application.Abstractions.Planning.ISizingInputs, NoSizing>(); // the down-only sizing (BK-10) reads the stores and the bars
        services.AddSingleton<XDrishti.Application.Abstractions.Market.Lists.IListFilterSettings, ListsOff>(); // the dated-list filter (DATA-01) needs its switch and the as-of port
        services.AddScoped<XDrishti.Application.Abstractions.Market.Lists.IListFlagsAsOf, NoLists>();
        services.AddScoped<IAutoScopeSource, NoScope>(); // the owner's scope (AU-16) reads the scope store and the baskets
        if (flag is { } value)
        {
            services.AddSingleton<IPlanBuilderSettings>(new Settings(value));
        }

        if (withAutoSwitch)
        {
            services.AddSingleton<XDrishti.Application.Abstractions.LearnedSettings.IAutoSwitch>(new AutoSwitch());
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

    [Fact]
    public void Where_the_auto_switch_can_be_read_the_builder_follows_it_at_build_time_and_the_configured_flag_still_wins() =>
        (Resolve(false, true).GetType().Name, Resolve(null, true).GetType().Name, Resolve(true, true).GetType().Name).ShouldBe(("SwitchedPlanBuilder", "SwitchedPlanBuilder", "SelectionPlanBuilder"));

    private sealed class AutoSwitch : XDrishti.Application.Abstractions.LearnedSettings.IAutoSwitch
    {
        public ValueTask<bool> IsOnAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }

    private sealed class ListsOff : XDrishti.Application.Abstractions.Market.Lists.IListFilterSettings
    {
        public bool Enabled => false;
    }

    private sealed class NoLists : XDrishti.Application.Abstractions.Market.Lists.IListFlagsAsOf
    {
        public ValueTask<XDrishti.Application.Abstractions.Market.Lists.ListFlags> ForAsync(IReadOnlyCollection<Guid> instrumentIds, DateOnly banDate, DateOnly surveillanceDate, CancellationToken cancellationToken) =>
            ValueTask.FromResult(XDrishti.Application.Abstractions.Market.Lists.ListFlags.None);
    }

    private sealed class NoSizing : XDrishti.Application.Abstractions.Planning.ISizingInputs
    {
        public ValueTask<XDrishti.Application.Abstractions.Planning.SizingInputs> ReadAsync(DateOnly session, DateOnly forDate, IReadOnlyList<XDrishti.Application.Abstractions.Planning.ContextInstrument> instruments, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new XDrishti.Application.Abstractions.Planning.SizingInputs(XDrishti.Domain.Selection.SizeScaling.SizingSettings.Defaults, 0, new Dictionary<string, XDrishti.Application.Abstractions.Planning.SymbolSizingInputs>()));
    }

    private sealed class NoScope : IAutoScopeSource
    {
        public ValueTask<ResolvedAutoScope?> ResolveAsync(CancellationToken cancellationToken) => ValueTask.FromResult<ResolvedAutoScope?>(null);
    }

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

    private sealed class NoDecisions : IEventFilterDecisions
    {
        public Task<IReadOnlyList<EventFilterRow>> RowsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EventFilterRow>>([]);

        public Task<(long? Id, string? Refused)> AppendAsync(NewEventFilterRow row, CancellationToken cancellationToken) => Task.FromResult<(long?, string?)>((null, "no store in this composition"));
    }

    private sealed class NoRows : ICalendarRows
    {
        public ValueTask<IReadOnlyList<CalendarRow>> ResultsRowsAsync(IReadOnlyCollection<Guid>? instrumentIds, DateTimeOffset cutoff, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<CalendarRow>>([]);
    }

    private sealed class PlainCalendar : ITradingCalendar
    {
        public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new TradingCalendar());

        public void Invalidate()
        {
        }
    }

    private sealed class NoSettings : IFilingSettings
    {
        public int LagMinutes => 5;

        public long MaxFilesBytes => 0;

        public long MinFreeBytes => 0;
    }
}
