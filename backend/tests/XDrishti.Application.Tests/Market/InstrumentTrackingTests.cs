using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Market;

/// <summary>The shared service (used by the endpoints and by baskets): it collects rows and an audit event, and never saves by itself.</summary>
public sealed class InstrumentTrackingTests
{
    private readonly TrackingFixture _f = new();

    private static CancellationToken Ct => TrackingFixture.Ct;

    [Fact]
    public async Task Tracking_collects_the_rows_and_one_audit_event_naming_the_caller_but_saves_nothing()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        var b = _f.Data.AddInstrument("NSE:BBB");

        var result = await _f.Tracking.TrackAsync([a.Id, b.Id, a.Id], TrackingFixture.Owner, _f.Clock.GetUtcNow(), "basket 'Core'", Ct);

        result.Value.ShouldBe(new TrackingOutcome(2, 2, 0, 0));
        result.Value.Changed.ShouldBe(2);
        _f.Data.Saves.ShouldBe(0);
        _f.Data.Tracked.ShouldBeEmpty(); // still in the unit of work, not saved
        _f.Data.Events.ShouldHaveSingleItem().Detail.ShouldBe("Tracked 2 of 2 instruments (2 new, 0 tracked again, 0 already tracked) via basket 'Core'");
    }

    [Fact]
    public async Task When_asked_to_rely_on_tracked_rows_the_ones_tracked_already_take_part_in_the_save_but_new_and_reactivated_rows_do_not()
    {
        var kept = _f.Data.AddInstrument("NSE:AAA");
        var again = _f.Data.AddInstrument("NSE:BBB");
        var fresh = _f.Data.AddInstrument("NSE:CCC");
        await _f.TrackOkAsync(kept.Id, again.Id);
        await _f.Untrack.HandleAsync(new UntrackInstrumentsCommand(new TrackingInput([again.Id]), TrackingFixture.Owner), Ct);

        await _f.Tracking.TrackAsync([kept.Id, again.Id, fresh.Id], TrackingFixture.Owner, _f.Clock.GetUtcNow(), null, Ct, relyOnTracked: true);

        _f.Data.Relied.ShouldBe([_f.Data.Tracked.Single(t => t.Id == kept.Id)]);
    }

    [Fact]
    public async Task Plain_tracking_does_not_rely_on_the_rows_that_are_tracked_already()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        await _f.TrackOkAsync(a.Id);

        await _f.Tracking.TrackAsync([a.Id], TrackingFixture.Owner, _f.Clock.GetUtcNow(), null, Ct);

        _f.Data.Relied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refusal_collects_nothing()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        var gone = _f.Data.AddInstrument("NSE:OLD", retired: true);

        var result = await _f.Tracking.TrackAsync([a.Id, gone.Id], TrackingFixture.Owner, _f.Clock.GetUtcNow(), null, Ct);

        result.Error.Code.ShouldBe("instruments.retired");
        _f.Data.Events.ShouldBeEmpty();
        await _f.Data.SaveAsync(Ct);
        _f.Data.Tracked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Untracking_through_the_service_collects_the_change_and_an_audit_event()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        await _f.TrackOkAsync(a.Id);
        _f.Data.Events.Clear();

        var result = await _f.Tracking.UntrackAsync([a.Id], TrackingFixture.Owner, _f.Clock.GetUtcNow(), "basket 'Core'", Ct);

        result.Value.ShouldBe(new UntrackOutcome(1, 1, 0));
        _f.Data.Events.ShouldHaveSingleItem().Event.ShouldBe(AccountEventType.InstrumentsUntracked);
    }

    [Fact]
    public async Task An_empty_list_is_a_no_op()
    {
        var result = await _f.Tracking.TrackAsync([], TrackingFixture.Owner, _f.Clock.GetUtcNow(), null, Ct);

        result.Value.ShouldBe(new TrackingOutcome(0, 0, 0, 0));
        _f.Data.Events.ShouldBeEmpty();
    }
}
