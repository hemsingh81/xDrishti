using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Planning;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec P7-09 on the real database: the stored context source never reads past the close of the session (truncation), and the suggestion inbox stores a plan, kind and subject once.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class PlanWiringIntegrationTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Session = new(2026, 10, 9);

    private static readonly DateOnly ForDate = new(2026, 10, 12);

    private static IEnumerable<DateOnly> Weekdays(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                yield return d;
            }
        }
    }

    private static Bar[] Series(IEnumerable<DateOnly> days, Func<int, double> price) => [.. days.Select((d, i) => ReplayDays.Flat(d, price(i))).SelectMany(b => b)];

    private async Task StoreAsync(Guid id, Bar[] bars)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBarStore>();
        await using var merge = await store.BeginMergeAsync(id, BarSource.Dhan, Ct);
        await merge.AddAsync(bars, Ct);
        await merge.CompleteAsync(null, Ct);
        var aggregates = scope.ServiceProvider.GetRequiredService<IBarAggregates>();
        await aggregates.MarkAllDirtyAsync(Ct);
        foreach (var dirty in await aggregates.ListDirtyAsync(Ct))
        {
            await aggregates.BuildAsync(dirty, Ct);
        }
    }

    private async Task<PlanContext> ContextAsync(params ContextInstrument[] instruments) => await ContextAsync(null, instruments);

    // With a setting given, the real StoredPlanContextSource is built over the real stores with that open-book setting (the factory's own config is the default, off).
    private async Task<PlanContext> ContextAsync(bool? assumedEmpty, params ContextInstrument[] instruments)
    {
        using var scope = Factory.Services.CreateScope();
        IPlanContextSource source = scope.ServiceProvider.GetRequiredService<IPlanContextSource>();
        if (assumedEmpty is { } value)
        {
            var type = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly.GetType("XDrishti.Infrastructure.Planning.StoredPlanContextSource", throwOnError: true)!;
            source = (IPlanContextSource)ActivatorUtilities.CreateInstance(scope.ServiceProvider, type, new FixedOpenBook(value));
        }

        var result = await source.ReadAsync(new PlanContextRequest(Session, ForDate, instruments), Ct);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private sealed class FixedOpenBook(bool assumedEmpty) : IOpenBookSettings
    {
        public bool AssumedEmpty => assumedEmpty;

        public bool UsePaperLedger => false;
    }

    [Fact]
    public async Task Bars_after_the_close_of_the_session_change_nothing_in_the_context()
    {
        var a = await SeedAsync("WIRA");
        var b = await SeedAsync("WIRB");
        var past = Weekdays(new DateOnly(2026, 8, 24), Session).ToList();
        await StoreAsync(a, Series(past, i => 100 + (i * 7 % 11)));
        await StoreAsync(b, Series(past, i => 50 + (i * 5 % 13)));
        var instruments = new[] { new ContextInstrument(a, "WIRA"), new ContextInstrument(b, "WIRB") };
        var before = await ContextAsync(instruments);

        var later = Weekdays(Session.AddDays(3), Session.AddDays(9)).ToList();
        await StoreAsync(a, Series(later, i => 500 + i * 40));
        await StoreAsync(b, Series(later, i => 1 + i));
        var after = await ContextAsync(instruments);

        // the same days changed (not only added): still nothing moves
        await StoreAsync(a, Series(later, i => 9000 - i * 3));
        await StoreAsync(b, Series(later, i => 7 + i * 11));
        var changed = await ContextAsync(instruments);

        changed.Correlations.ShouldNotBeNull().Get("WIRA", "WIRB").ShouldBe(before.Correlations!.Get("WIRA", "WIRB"));
        before.Correlations.ShouldNotBeNull().Get("WIRA", "WIRB").ShouldNotBeNull();
        after.Correlations.ShouldNotBeNull().Get("WIRA", "WIRB").ShouldBe(before.Correlations.Get("WIRA", "WIRB"));
        after.Instruments.Keys.ShouldBe(before.Instruments.Keys);
        after.Book.ShouldBe(before.Book);
        after.Gate.ShouldBeNull();
        after.TakenTrades.ShouldBeNull();
    }

    [Fact]
    public async Task By_default_the_context_says_the_open_book_is_unavailable_and_has_no_market_figures()
    {
        var context = await ContextAsync();

        context.Book.ShouldBeNull();
        context.Notes.ShouldContain(note => note.StartsWith("Open book unavailable", StringComparison.Ordinal));
        context.Gate.ShouldBeNull();
        context.Accounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_owner_setting_the_real_context_source_returns_the_empty_book_and_says_so()
    {
        var context = await ContextAsync(true);

        context.Book.ShouldNotBeNull().ShouldBeEmpty();
        context.Notes.ShouldContain(PlanContextNotes.NoBookNote);
        context.Notes.ShouldContain(PlanContextNotes.NoAccountNote);
        context.Gate.ShouldBeNull();
    }

    [Fact]
    public async Task A_suggestion_is_stored_once_per_plan_kind_and_subject()
    {
        using var scope = Factory.Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<IPlanStore>();
        var published = await plans.PublishAsync(new NewPlan("wiring-suggest", ForDate, new DateTimeOffset(2026, 10, 12, 3, 35, 0, TimeSpan.Zero), 1, new string('d', 64), "b", true, "test", DateTimeOffset.UnixEpoch, []), Ct);
        var store = scope.ServiceProvider.GetRequiredService<ISuggestionStore>();
        NewSuggestion[] suggestions = [new(SuggestionKinds.NoPrimaryTickets, "plan", "none"), new(SuggestionKinds.TicketsHeldUnavailableInput, "cost", "held")];

        var first = await store.InsertAsync(published.Id, suggestions, DateTimeOffset.UnixEpoch, Ct);
        var again = await store.InsertAsync(published.Id, suggestions, DateTimeOffset.UnixEpoch, Ct);

        first.ShouldBe(2);
        again.ShouldBe(0);
        (await store.ForPlanAsync(published.Id, Ct)).Select(s => s.Suggestion).ShouldBe(suggestions);
    }
}
