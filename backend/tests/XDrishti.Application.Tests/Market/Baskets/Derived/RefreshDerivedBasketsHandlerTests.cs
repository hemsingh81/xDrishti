using System.Text.Json;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Tests.Market.Baskets.Derived;

public sealed class RefreshDerivedBasketsHandlerTests
{
    private static readonly RefreshDerivedBasketsCommand Run = new("system:test", false);

    [Fact]
    public async Task Nothing_is_read_or_written_while_the_setting_is_off()
    {
        var world = DerivedWorld.WithLiquidBasket(5);
        world.Settings.Enabled = false;

        var result = await world.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(BasketErrors.DerivedDisabledCode);
        world.Universe.Reads.ShouldBe(0);
        world.Store.ListCalls.ShouldBe(0);
        world.Store.Versions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_writes_one_version_effective_from_the_next_session_with_its_counts()
    {
        var world = DerivedWorld.WithLiquidBasket(120);

        var result = await world.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var version = world.Store.Versions.ShouldHaveSingleItem();
        version.AsOf.ShouldBe(DerivedWorld.Friday);
        version.EffectiveFrom.ShouldBe(new DateOnly(2026, 10, 26)); // Saturday and Sunday skipped
        version.Provenance.ShouldBe(BasketProvenance.Derived);
        version.ParamsHash.ShouldNotBeNullOrEmpty();
        version.UniverseSize.ShouldBe(120);
        version.Scores.Count(s => s.IsMember).ShouldBe(100);
        version.Scores.Count.ShouldBe(120); // the 100 members and the next 20 by rank
        (version.Scores.Count(s => s.IsMember) + version.BelowThreshold + version.ExcludedCount).ShouldBe(version.UniverseSize);
    }

    [Fact]
    public async Task The_effective_date_comes_from_the_calendar_when_the_next_weekday_is_a_holiday()
    {
        var world = DerivedWorld.WithLiquidBasket(10);
        world.Calendar = new FakeCalendar(new DateOnly(2026, 10, 26));

        await world.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken);

        world.Store.Versions.ShouldHaveSingleItem().EffectiveFrom.ShouldBe(new DateOnly(2026, 10, 27));
    }

    [Fact]
    public async Task A_second_run_for_the_same_date_and_parameters_writes_nothing()
    {
        var world = DerivedWorld.WithLiquidBasket(10);
        var handler = world.Refresh();
        await handler.HandleAsync(Run, TestContext.Current.CancellationToken);

        var second = await handler.HandleAsync(Run, TestContext.Current.CancellationToken);

        world.Store.Versions.Count.ShouldBe(1);
        second.Value.Baskets.ShouldHaveSingleItem().Outcome.ShouldBe(VersionOutcomes.Unchanged);
    }

    [Fact]
    public async Task A_changed_parameter_set_writes_a_new_version_and_keeps_the_older_one()
    {
        var world = DerivedWorld.WithLiquidBasket(30);
        await world.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken);
        var changed = await world.SetParams().HandleAsync(new SetDerivedParamsCommand(world.Liquid.BasketId, new Dictionary<string, double> { ["Basket3.TopK"] = 10 }, null, "owner"), TestContext.Current.CancellationToken);
        changed.IsSuccess.ShouldBeTrue();

        await world.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken);

        world.Store.Versions.Count.ShouldBe(2);
        world.Store.Versions.Select(v => v.ParamsHash).Distinct().Count().ShouldBe(2);
        world.Store.Versions[1].Scores.Count(s => s.IsMember).ShouldBe(10);
        world.Store.Versions[0].Scores.Count(s => s.IsMember).ShouldBe(30); // the older version is untouched
    }

    [Fact]
    public async Task The_next_run_takes_its_incumbents_from_the_previous_versions_members()
    {
        // ten slots, buffer up to rank 15: an incumbent that slips to rank 12 stays, a newcomer at rank 10 does not take its place.
        var world = DerivedWorld.WithLiquidBasket(20, topK: 10);
        await world.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken);
        var first = world.Store.Versions.Single();
        var incumbentRank10 = world.Universe.Instruments.First(i => i.InstrumentId == first.Scores.Single(s => s.IsMember && s.Rank == 10).InstrumentId);
        var challenger = world.Universe.Instruments.First(i => i.InstrumentId == first.Scores.Single(s => !s.IsMember && s.Rank == 11).InstrumentId);
        // the next session: the challenger now trades more than the incumbent of rank 10 (it ranks 10, the incumbent 11)
        var nextDay = new DateOnly(2026, 10, 26);
        foreach (var instrument in world.Universe.Instruments.ToList())
        {
            var volume = instrument.InstrumentId == challenger.InstrumentId ? incumbentRank10.Bars.Daily[^1].Volume : instrument.InstrumentId == incumbentRank10.InstrumentId ? challenger.Bars.Daily[^1].Volume : instrument.Bars.Daily[^1].Volume;
            world.Universe.Instruments[world.Universe.Instruments.IndexOf(instrument)] = instrument with { Bars = instrument.Bars with { Daily = [.. instrument.Bars.Daily.Select(b => b with { Volume = volume }), new(nextDay, 100, 101, 99, 100, volume)] } };
        }

        await world.Refresh().HandleAsync(new RefreshDerivedBasketsCommand("system:test", false, nextDay), TestContext.Current.CancellationToken);

        var second = world.Store.Versions[1];
        var members = second.Scores.Where(s => s.IsMember).Select(s => s.InstrumentId).ToHashSet();
        members.Count.ShouldBe(10);
        members.ShouldContain(incumbentRank10.InstrumentId); // an incumbent within the buffer keeps its slot
        members.ShouldNotContain(challenger.InstrumentId); // it now outranks the incumbent, but no slot is free: without the previous members it would have swapped in
    }

    [Fact]
    public async Task Bars_after_the_date_do_not_change_the_stored_version()
    {
        var plain = DerivedWorld.WithLiquidBasket(30, topK: 10);
        var later = DerivedWorld.WithLiquidBasket(30, topK: 10);
        for (var i = 0; i < later.Universe.Instruments.Count; i++)
        {
            var instrument = later.Universe.Instruments[i];
            later.Universe.Instruments[i] = instrument with { Bars = instrument.Bars with { Daily = [.. instrument.Bars.Daily, new(DerivedWorld.Friday.AddDays(3), 100, 900, 1, 500, 99_000_000)] } };
        }

        await plain.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken);
        await later.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken);

        later.Store.Versions.Single().Scores.Select(s => (s.Rank, s.Score, s.IsMember)).ShouldBe(plain.Store.Versions.Single().Scores.Select(s => (s.Rank, s.Score, s.IsMember)));
    }

    [Fact]
    public async Task An_instrument_without_a_bar_on_the_date_is_not_in_the_universe()
    {
        var world = DerivedWorld.WithLiquidBasket(5);
        world.AddInstrument("RETIRED", 9_999, 70, new DateOnly(2026, 10, 20));

        await world.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken);

        world.Store.Versions.Single().UniverseSize.ShouldBe(5);
    }

    [Fact]
    public async Task An_instrument_with_too_little_history_is_excluded_by_name_and_counted()
    {
        var world = DerivedWorld.WithLiquidBasket(5);
        var young = world.AddInstrument("YOUNG", 9_999, 10);

        await world.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken);

        var version = world.Store.Versions.Single();
        version.UniverseSize.ShouldBe(6);
        version.ExcludedCount.ShouldBe(1);
        JsonDocument.Parse(version.ExcludedJson).RootElement[0].GetProperty("Reason").GetString().ShouldBe("insufficient_history");
        JsonDocument.Parse(version.ExcludedJson).RootElement[0].GetProperty("InstrumentId").GetGuid().ShouldBe(young.InstrumentId);
        (version.Scores.Count(s => s.IsMember) + version.BelowThreshold + version.ExcludedCount).ShouldBe(version.UniverseSize);
    }

    [Fact]
    public async Task A_basket_that_is_switched_off_is_not_formed()
    {
        var world = DerivedWorld.WithLiquidBasket(5, enabled: false);

        await world.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken);

        world.Store.Versions.ShouldBeEmpty();
        world.Universe.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Basket_13_is_skipped_whatever_its_switch_says_because_its_flags_are_unavailable()
    {
        var world = DerivedWorld.WithLiquidBasket(5);
        world.Store.Definitions.Add(new(Guid.NewGuid(), "Derived: Intraday-tradable (proxy)", 13, true, null, new Dictionary<string, double>()));

        var run = await world.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken);

        run.Value.Baskets.Single(b => b.Name.Contains("proxy", StringComparison.Ordinal)).Reason.ShouldBe("flags_unavailable");
        world.Store.Versions.ShouldHaveSingleItem(); // only the liquid basket
    }

    [Fact]
    public async Task A_version_behind_a_newer_published_one_is_not_written()
    {
        var world = DerivedWorld.WithLiquidBasket(5);
        await world.Refresh().HandleAsync(new RefreshDerivedBasketsCommand("system:test", false, new DateOnly(2026, 10, 23)), TestContext.Current.CancellationToken);

        var older = await world.Refresh().HandleAsync(new RefreshDerivedBasketsCommand("system:test", false, new DateOnly(2026, 10, 22)), TestContext.Current.CancellationToken);

        older.Value.Baskets.Single().Reason.ShouldBe("behind_published_version");
        world.Store.Versions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_run_in_progress_is_refused_not_queued()
    {
        var world = DerivedWorld.WithLiquidBasket(5);
        world.Lock.Held = true;

        var result = await world.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(BasketErrors.DerivedRunningCode);
        world.Universe.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task A_failing_write_surfaces_as_the_exception_of_the_run_and_leaves_no_version()
    {
        var world = DerivedWorld.WithLiquidBasket(5);
        world.Store.ThrowOnWrite = true;

        await Should.ThrowAsync<InvalidOperationException>(async () => await world.Refresh().HandleAsync(Run, TestContext.Current.CancellationToken));

        world.Store.Versions.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_scheduled_service_ends_as_skipped_while_the_setting_is_off()
    {
        var world = DerivedWorld.WithLiquidBasket(5);
        world.Settings.Enabled = false;
        var scopes = new XDrishti.Application.Tests.SimAll.FakeScopes((typeof(XDrishti.Application.Abstractions.Messaging.ICommandHandler<RefreshDerivedBasketsCommand, DerivedRun>), world.Refresh()));
        var service = new DerivedBasketsService(scopes);
        var context = new XDrishti.Application.Abstractions.Platform.ServiceRunContext(
            Guid.NewGuid(), XDrishti.Domain.Platform.Services.ServiceRunTrigger.Scheduled, 1, "system", new Dictionary<string, JsonElement>(), (_, _) => ValueTask.CompletedTask);

        var outcome = await service.RunAsync(context, TestContext.Current.CancellationToken);

        outcome.Status.ShouldBe(XDrishti.Domain.Platform.Services.ServiceRunStatus.Skipped);
        outcome.Message.ShouldBe("disabled");
        world.Universe.Reads.ShouldBe(0);
    }
}
