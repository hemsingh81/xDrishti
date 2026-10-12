using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Market;

public sealed class UntrackInstrumentsHandlerTests
{
    private readonly TrackingFixture _f = new();

    private static CancellationToken Ct => TrackingFixture.Ct;

    private Task<Result<UntrackResult>> UntrackAsync(IReadOnlyList<Guid>? ids) =>
        _f.Untrack.HandleAsync(new UntrackInstrumentsCommand(new TrackingInput(ids), TrackingFixture.Owner), Ct).AsTask();

    [Fact]
    public async Task Untracking_switches_the_row_off_keeps_it_and_is_audited_with_counts()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        var b = _f.Data.AddInstrument("NSE:BBB");
        var c = _f.Data.AddInstrument("NSE:CCC");
        await _f.TrackOkAsync(a.Id, b.Id);
        _f.Data.Events.Clear();
        _f.Clock.Advance(TimeSpan.FromHours(2));

        var result = await UntrackAsync([a.Id, c.Id]);

        result.Value.ShouldBe(new UntrackResult(2, 1, 1, 1));
        _f.Data.Tracked.Count.ShouldBe(2);
        var row = _f.Data.Tracked.Single(t => t.Id == a.Id);
        (row.Active, row.DeactivatedAt).ShouldBe((false, _f.Clock.GetUtcNow()));
        _f.Data.Tracked.Single(t => t.Id == b.Id).Active.ShouldBeTrue();
        var audit = _f.Data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.AccountId).ShouldBe((AccountEventType.InstrumentsUntracked, null));
        audit.Detail.ShouldBe("Untracked 1 of 2 instruments (1 were not tracked)");
    }

    [Fact]
    public async Task Untracking_twice_changes_nothing_the_second_time_and_writes_no_audit_entry()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        await _f.TrackOkAsync(a.Id);
        await UntrackAsync([a.Id]);
        var stamp = _f.Data.Tracked.Single().DeactivatedAt;
        _f.Data.Events.Clear();
        _f.Clock.Advance(TimeSpan.FromDays(1));

        var again = await UntrackAsync([a.Id, a.Id]);

        again.Value.ShouldBe(new UntrackResult(1, 0, 1, 0));
        _f.Data.Tracked.Single().DeactivatedAt.ShouldBe(stamp);
        _f.Data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_retired_instrument_can_be_untracked()
    {
        var gone = _f.Data.AddInstrument("NSE:OLD", retired: true);
        _f.Data.Tracked.Add(XDrishti.Domain.Market.TrackedInstrument.Track(gone.Id, TrackingFixture.Owner, _f.Clock.GetUtcNow()));

        var result = await UntrackAsync([gone.Id]);

        result.Value.ShouldBe(new UntrackResult(1, 1, 0, 0));
    }

    [Fact]
    public async Task An_unknown_id_fails_the_whole_call_and_changes_nothing()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        await _f.TrackOkAsync(a.Id);
        var saves = _f.Data.Saves;

        var result = await UntrackAsync([a.Id, Guid.CreateVersion7()]);

        result.Error.Code.ShouldBe("instruments.invalid");
        _f.Data.Tracked.Single().Active.ShouldBeTrue();
        _f.Data.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task A_save_refused_because_the_row_changed_is_read_again_and_the_untrack_is_applied_to_what_is_there_now()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        await _f.TrackOkAsync(a.Id);
        var row = _f.Data.Tracked.Single();
        _f.Data.Collisions.Enqueue(() => row.Reactivate("other", _f.Clock.GetUtcNow())); // the first try's change is undone, as the refused save does in the database

        var result = await UntrackAsync([a.Id]);

        result.Value.ShouldBe(new UntrackResult(1, 1, 0, 0));
        _f.Data.Tracked.Single().Active.ShouldBeFalse();
    }

    [Fact]
    public async Task Repeated_refused_saves_end_in_a_conflict_instead_of_looping()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        await _f.TrackOkAsync(a.Id);
        var saves = _f.Data.Saves;
        for (var i = 0; i < 5; i++)
        {
            _f.Data.Collisions.Enqueue(() => { });
        }

        var result = await UntrackAsync([a.Id]);

        result.Error.Code.ShouldBe("instruments.tracking_collided");
        _f.Data.Saves.ShouldBe(saves + 3);
    }

    [Fact]
    public async Task An_empty_oversized_or_missing_list_is_rejected()
    {
        (await UntrackAsync(null)).Error.Code.ShouldBe("instruments.invalid");
        (await UntrackAsync([])).IsFailure.ShouldBeTrue();
        (await UntrackAsync([.. Enumerable.Range(0, 501).Select(_ => Guid.CreateVersion7())])).Error.Fields!.Single().Message.ShouldContain("500");
    }
}
