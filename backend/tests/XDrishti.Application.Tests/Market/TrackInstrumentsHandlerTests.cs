using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market;

public sealed class TrackInstrumentsHandlerTests
{
    private readonly TrackingFixture _f = new();

    private static CancellationToken Ct => TrackingFixture.Ct;

    private Task<Result<TrackResult>> TrackAsync(IReadOnlyList<Guid>? ids) =>
        _f.Track.HandleAsync(new TrackInstrumentsCommand(new TrackingInput(ids), TrackingFixture.Owner), Ct).AsTask();

    [Fact]
    public async Task New_instruments_get_a_tracking_row_that_asks_for_history_and_the_call_is_audited_with_counts()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        var b = _f.Data.AddInstrument("NSE:BBB");

        var result = await _f.TrackOkAsync(a.Id, b.Id);

        result.ShouldBe(new TrackResult(2, 2, 0, 0, 2));
        _f.Data.Tracked.Select(t => t.Id).Order().ShouldBe(new[] { a.Id, b.Id }.Order());
        _f.Data.Tracked.ShouldAllBe(t => t.Active && t.BackfillRequestedAt == _f.Clock.GetUtcNow() && t.TrackedBy == TrackingFixture.Owner);
        var audit = _f.Data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.AccountId, audit.Actor).ShouldBe((AccountEventType.InstrumentsTracked, null, TrackingFixture.Owner));
        audit.Detail.ShouldBe("Tracked 2 of 2 instruments (2 new, 0 tracked again, 0 already tracked)");
    }

    [Fact]
    public async Task Tracking_twice_is_a_no_op_the_second_time_and_writes_no_audit_entry()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        await _f.TrackOkAsync(a.Id);
        _f.Data.Events.Clear();

        var again = await _f.TrackOkAsync(a.Id);

        again.ShouldBe(new TrackResult(1, 0, 0, 1, 1));
        _f.Data.Tracked.Count.ShouldBe(1);
        _f.Data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Duplicate_ids_count_once()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");

        var result = await _f.TrackOkAsync(a.Id, a.Id, a.Id);

        result.ShouldBe(new TrackResult(1, 1, 0, 0, 1));
        _f.Data.Tracked.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Tracking_again_after_an_untrack_reactivates_the_row_and_keeps_its_history()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        await _f.TrackOkAsync(a.Id);
        var row = _f.Data.Tracked.Single();
        var firstRequest = row.BackfillRequestedAt;
        row.Deactivate(_f.Clock.GetUtcNow());
        _f.Clock.Advance(TimeSpan.FromDays(3));

        var result = await _f.TrackOkAsync(a.Id);

        result.ShouldBe(new TrackResult(1, 0, 1, 0, 1));
        _f.Data.Tracked.ShouldHaveSingleItem().ShouldBeSameAs(row);
        (row.Active, row.ActiveSince, row.DeactivatedAt, row.BackfillRequestedAt).ShouldBe((true, _f.Clock.GetUtcNow(), null, _f.Clock.GetUtcNow()));
        row.BackfillRequestedAt.ShouldNotBe(firstRequest);
        _f.Data.Events.Last().Detail.ShouldBe("Tracked 1 of 1 instruments (0 new, 1 tracked again, 0 already tracked)");
    }

    [Fact]
    public async Task A_retired_instrument_is_refused_naming_it_and_nothing_at_all_is_tracked()
    {
        var live = _f.Data.AddInstrument("NSE:AAA");
        var gone = _f.Data.AddInstrument("NSE:OLD", retired: true);

        var result = await TrackAsync([live.Id, gone.Id]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("instruments.retired");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Fields!.Single().Message.ShouldContain("NSE:OLD");
        _f.Data.Tracked.ShouldBeEmpty();
        _f.Data.Events.ShouldBeEmpty();
        _f.Data.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Many_retired_instruments_are_summarised_in_the_message()
    {
        var ids = Enumerable.Range(0, 7).Select(i => _f.Data.AddInstrument($"NSE:OLD{i}", retired: true).Id).ToList();

        var result = await TrackAsync(ids);

        result.Error.Fields!.Single().Message.ShouldContain("and 2 more");
    }

    [Fact]
    public async Task An_instrument_tracked_before_it_retired_stays_tracked_and_is_not_an_error()
    {
        var retired = _f.Data.AddInstrument("NSE:AAA", retired: true);
        var row = TrackedInstrument.Track(retired.Id, TrackingFixture.Owner, _f.Clock.GetUtcNow());
        _f.Data.Tracked.Add(row);

        var result = await _f.TrackOkAsync(retired.Id);

        result.ShouldBe(new TrackResult(1, 0, 0, 1, 1));
        row.Active.ShouldBeTrue();
        _f.Data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_retired_instrument_that_was_untracked_cannot_be_tracked_again()
    {
        var retired = _f.Data.AddInstrument("NSE:OLD", retired: true);
        var row = TrackedInstrument.Track(retired.Id, TrackingFixture.Owner, _f.Clock.GetUtcNow());
        row.Deactivate(_f.Clock.GetUtcNow());
        _f.Data.Tracked.Add(row);

        var result = await TrackAsync([retired.Id]);

        result.Error.Code.ShouldBe("instruments.retired");
        row.Active.ShouldBeFalse();
    }

    [Fact]
    public async Task An_unknown_id_fails_the_whole_call()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");

        var result = await TrackAsync([a.Id, Guid.CreateVersion7()]);

        result.Error.Code.ShouldBe("instruments.invalid");
        result.Error.Fields!.Single().Field.ShouldBe("instrumentIds");
        _f.Data.Tracked.ShouldBeEmpty();
        _f.Data.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Nothing_to_track_is_rejected()
    {
        (await TrackAsync(null)).Error.Code.ShouldBe("instruments.invalid");
        (await TrackAsync([])).Error.Fields!.Single().Field.ShouldBe("instrumentIds");
        (await TrackAsync([Guid.Empty])).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task At_most_five_hundred_ids_are_accepted_in_one_call()
    {
        var ids = Enumerable.Range(0, 500).Select(_ => _f.Data.AddInstrument($"NSE:{Guid.NewGuid():N}").Id).ToList();

        (await TrackAsync(ids)).IsSuccess.ShouldBeTrue();

        var tooMany = await TrackAsync([.. ids, _f.Data.AddInstrument("NSE:ONE-MORE").Id]);
        tooMany.Error.Code.ShouldBe("instruments.invalid");
        tooMany.Error.Fields!.Single().Message.ShouldContain("500");
        _f.Data.Tracked.Count.ShouldBe(500);
    }

    [Fact]
    public async Task A_save_that_collides_with_a_concurrent_request_is_read_again_and_counts_the_instrument_as_already_tracked()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        _f.Data.Collisions.Enqueue(() => _f.Data.Tracked.Add(TrackedInstrument.Track(a.Id, "other", _f.Clock.GetUtcNow())));

        var result = await _f.TrackOkAsync(a.Id);

        result.ShouldBe(new TrackResult(1, 0, 0, 1, 1));
        _f.Data.Tracked.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Repeated_collisions_end_in_a_conflict_instead_of_looping()
    {
        var a = _f.Data.AddInstrument("NSE:AAA");
        for (var i = 0; i < 5; i++)
        {
            _f.Data.Collisions.Enqueue(() => { });
        }

        var result = await TrackAsync([a.Id]);

        result.Error.Code.ShouldBe("instruments.tracking_collided");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        _f.Data.Saves.ShouldBe(3);
    }
}
