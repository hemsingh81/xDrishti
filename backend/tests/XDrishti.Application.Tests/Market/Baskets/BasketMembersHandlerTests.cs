using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Tests.Market.Baskets;

public sealed class BasketMembersHandlerTests
{
    private readonly BasketFixture _f = new();

    private static CancellationToken Ct => BasketFixture.Ct;

    [Fact]
    public async Task Added_instruments_are_appended_in_the_given_order_tracked_in_the_same_save_and_audited_with_counts()
    {
        var basket = _f.Basket("Core");
        var ids = _f.Instruments("NSE:AAA", "NSE:BBB", "NSE:CCC");

        var result = await _f.AddOkAsync(basket, ids[2], ids[0], ids[1]);

        result.ShouldBe(new AddMembersResult(3, 3, 0, 3, 3));
        _f.Data.ActiveInstruments(basket).ShouldBe([ids[2], ids[0], ids[1]]);
        _f.Data.Members.ShouldAllBe(m => m.AddedBy == BasketFixture.Owner && m.ValidFrom == _f.Clock.GetUtcNow());
        _f.Master.Tracked.Select(t => t.Id).Order().ShouldBe(ids.Order());
        _f.Data.Saves.ShouldBe(1, "baskets, members, tracking rows and audit entries are saved together");
        _f.Master.Events.Select(e => e.Event).ShouldBe([AccountEventType.InstrumentsTracked, AccountEventType.BasketMembersAdded]);
        _f.Master.Events[0].Detail.ShouldBe("Tracked 3 of 3 instruments (3 new, 0 tracked again, 0 already tracked) via basket 'Core'");
        _f.Master.Events[1].Detail.ShouldBe("Added 3 of 3 instruments to basket 'Core' (0 were members already, 3 tracked now)");
        basket.Revision.ShouldBe(1);
    }

    [Fact]
    public async Task Adding_what_is_already_there_is_idempotent_and_saves_nothing()
    {
        var basket = _f.Basket("Core");
        var ids = _f.Instruments("NSE:AAA", "NSE:BBB");
        await _f.AddOkAsync(basket, ids);
        _f.Master.Events.Clear();
        var saves = _f.Data.Saves;

        var again = await _f.AddOkAsync(basket, ids[1], ids[0], ids[0]);

        again.ShouldBe(new AddMembersResult(2, 0, 2, 0, 2));
        _f.Data.Saves.ShouldBe(saves);
        _f.Master.Events.ShouldBeEmpty();
        _f.Data.Members.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Only_the_new_instruments_are_added_and_appended_after_the_current_members()
    {
        var basket = _f.Basket("Core");
        var ids = _f.Instruments("NSE:AAA", "NSE:BBB", "NSE:CCC");
        await _f.AddOkAsync(basket, ids[0]);

        var result = await _f.AddOkAsync(basket, ids[0], ids[2], ids[1]);

        result.ShouldBe(new AddMembersResult(3, 2, 1, 2, 3));
        _f.Data.ActiveInstruments(basket).ShouldBe([ids[0], ids[2], ids[1]]);
    }

    [Fact]
    public async Task An_instrument_that_is_tracked_already_is_not_counted_as_newly_tracked()
    {
        var basket = _f.Basket("Core");
        var ids = _f.Instruments("NSE:AAA", "NSE:BBB");
        _f.Track(ids[0]);

        var result = await _f.AddOkAsync(basket, ids);

        result.NewlyTracked.ShouldBe(1);
        _f.Master.Tracked.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_retired_instrument_that_is_not_tracked_is_refused_naming_it_and_nothing_at_all_is_applied()
    {
        var basket = _f.Basket("Core");
        var live = _f.Master.AddInstrument("NSE:AAA");
        var gone = _f.Master.AddInstrument("NSE:OLD", retired: true);

        var result = await _f.AddAsync(basket, live.Id, gone.Id);

        result.Error.Code.ShouldBe("instruments.retired");
        result.Error.Fields!.Single().Message.ShouldContain("NSE:OLD");
        _f.Data.Members.ShouldBeEmpty();
        _f.Master.Tracked.ShouldBeEmpty();
        _f.Master.Events.ShouldBeEmpty();
        _f.Data.Saves.ShouldBe(0);
        basket.Revision.ShouldBe(0);
    }

    [Fact]
    public async Task A_retired_instrument_that_is_still_tracked_can_be_added()
    {
        var basket = _f.Basket("Core");
        var retired = _f.Master.AddInstrument("NSE:OLD", retired: true);
        _f.Track(retired.Id);

        var result = await _f.AddOkAsync(basket, retired.Id);

        result.ShouldBe(new AddMembersResult(1, 1, 0, 0, 1));
    }

    [Fact]
    public async Task An_unknown_instrument_fails_the_whole_call()
    {
        var basket = _f.Basket("Core");
        var live = _f.Master.AddInstrument("NSE:AAA");

        var result = await _f.AddAsync(basket, live.Id, Guid.NewGuid());

        result.Error.Code.ShouldBe("instruments.invalid");
        _f.Data.Members.ShouldBeEmpty();
        _f.Data.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Bad_lists_notes_and_baskets_are_rejected()
    {
        var basket = _f.Basket("Core");
        var id = _f.Master.AddInstrument("NSE:AAA").Id;

        foreach (var ids in new IReadOnlyList<Guid>?[] { null, [], [Guid.Empty], [.. Enumerable.Range(0, 501).Select(_ => Guid.NewGuid())] })
        {
            var result = await _f.Add.HandleAsync(new AddBasketMembersCommand(basket.Id, new AddMembersInput(ids, null), BasketFixture.Owner), Ct);
            result.Error.Code.ShouldBe("baskets.invalid");
            result.Error.Fields!.Single().Field.ShouldBe("instrumentIds");
        }

        var longNote = await _f.Add.HandleAsync(new AddBasketMembersCommand(basket.Id, new AddMembersInput([id], new string('a', 501)), BasketFixture.Owner), Ct);
        longNote.Error.Fields!.Single().Field.ShouldBe("note");
        (await _f.Add.HandleAsync(new AddBasketMembersCommand(Guid.NewGuid(), new AddMembersInput([id], null), BasketFixture.Owner), Ct)).Error.Code.ShouldBe("baskets.not_found");
        _f.Data.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task An_archived_basket_takes_no_members()
    {
        var basket = _f.Basket("Core");
        var id = _f.Master.AddInstrument("NSE:AAA").Id;
        basket.Archive(_f.Clock.GetUtcNow());

        var result = await _f.AddAsync(basket, id);

        result.Error.Code.ShouldBe("baskets.archived");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task A_basket_holds_at_most_two_thousand_instruments()
    {
        var basket = _f.Basket("Core");
        for (var i = 0; i < BasketLimits.MaxActiveMembers - 1; i++)
        {
            _f.Data.AddMember(basket, Guid.NewGuid(), _f.Clock.GetUtcNow());
        }

        var ids = _f.Instruments("NSE:AAA", "NSE:BBB");
        var tooMany = await _f.AddAsync(basket, ids);
        var fits = await _f.AddAsync(basket, ids[0]);

        tooMany.Error.Code.ShouldBe("baskets.too_many_members");
        tooMany.Error.Fields!.Single().Message.ShouldContain("2,000");
        fits.IsSuccess.ShouldBeTrue();
        fits.Value.MemberCount.ShouldBe(BasketLimits.MaxActiveMembers);
    }

    [Fact]
    public async Task The_note_is_stored_on_each_new_membership()
    {
        var basket = _f.Basket("Core");
        var ids = _f.Instruments("NSE:AAA", "NSE:BBB");

        await _f.Add.HandleAsync(new AddBasketMembersCommand(basket.Id, new AddMembersInput(ids, "  from the scan "), BasketFixture.Owner), Ct);

        _f.Data.Members.ShouldAllBe(m => m.Note == "from the scan");
    }

    [Fact]
    public async Task An_add_that_collides_with_a_concurrent_add_of_the_same_instruments_reads_again_and_counts_them_as_members()
    {
        var basket = _f.Basket("Core");
        var ids = _f.Instruments("NSE:AAA", "NSE:BBB");
        _f.Data.Collisions.Enqueue(() => _f.Data.AddMember(basket, ids[0], _f.Clock.GetUtcNow()));

        var result = await _f.AddOkAsync(basket, ids);

        result.ShouldBe(new AddMembersResult(2, 1, 1, 1, 2));
        _f.Data.Members.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_add_depends_on_the_tracked_row_it_relied_on_and_when_a_concurrent_untrack_changed_it_the_instrument_is_tracked_again()
    {
        var basket = _f.Basket("Core");
        var id = _f.Instruments("NSE:AAA")[0];
        _f.Track(id);
        var row = _f.Master.Tracked.Single();
        _f.Data.Collisions.Enqueue(() => row.Deactivate(_f.Clock.GetUtcNow())); // the untrack that commits between the read and the save

        var result = await _f.AddOkAsync(basket, id);

        _f.Master.ReliedOnWhenRefused.ShouldBe([row]);
        result.ShouldBe(new AddMembersResult(1, 1, 0, 1, 1));
        row.Active.ShouldBeTrue();
        _f.Data.ActiveInstruments(basket).ShouldBe([id]);
    }

    [Fact]
    public async Task A_reactivated_tracking_row_is_counted_as_newly_tracked_and_the_stay_is_a_new_row()
    {
        var basket = _f.Basket("Core");
        var id = _f.Instruments("NSE:AAA")[0];
        await _f.AddOkAsync(basket, id);
        await _f.RemoveAsync(basket, id);
        _f.Master.Tracked.Single().Deactivate(_f.Clock.GetUtcNow());
        _f.Clock.Advance(TimeSpan.FromDays(2));

        var result = await _f.AddOkAsync(basket, id);

        result.ShouldBe(new AddMembersResult(1, 1, 0, 1, 1));
        _f.Data.Members.Count.ShouldBe(2);
        _f.Data.Members.Count(m => m.IsActive).ShouldBe(1);
    }

    [Fact]
    public async Task Removing_ends_the_stay_keeps_the_row_does_not_untrack_and_is_audited_with_counts()
    {
        var basket = _f.Basket("Core");
        var ids = _f.Instruments("NSE:AAA", "NSE:BBB", "NSE:CCC");
        await _f.AddOkAsync(basket, ids);
        _f.Master.Events.Clear();
        _f.Clock.Advance(TimeSpan.FromDays(1));

        var result = await _f.RemoveAsync(basket, ids[0], ids[2], Guid.NewGuid());

        result.Value.ShouldBe(new RemoveMembersResult(3, 2, 1, 1));
        _f.Data.Members.Count.ShouldBe(3);
        _f.Data.Members.Where(m => !m.IsActive).ShouldAllBe(m => m.ValidTo == _f.Clock.GetUtcNow() && m.RemovedBy == BasketFixture.Owner);
        _f.Data.ActiveInstruments(basket).ShouldBe([ids[1]]);
        _f.Master.Tracked.ShouldAllBe(t => t.Active);
        var audit = _f.Master.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Detail).ShouldBe((AccountEventType.BasketMembersRemoved, "Removed 2 of 3 instruments from basket 'Core' (1 were not members)"));
    }

    [Fact]
    public async Task Removing_twice_changes_nothing_the_second_time()
    {
        var basket = _f.Basket("Core");
        var id = _f.Instruments("NSE:AAA")[0];
        await _f.AddOkAsync(basket, id);
        await _f.RemoveAsync(basket, id);
        _f.Master.Events.Clear();
        var saves = _f.Data.Saves;

        var again = await _f.RemoveAsync(basket, id);

        again.Value.ShouldBe(new RemoveMembersResult(1, 0, 1, 0));
        _f.Data.Saves.ShouldBe(saves);
        _f.Master.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_from_an_unknown_or_archived_basket_fails_and_bad_lists_are_rejected()
    {
        var basket = _f.Basket("Core");
        var archived = _f.Basket("Old");
        archived.Archive(_f.Clock.GetUtcNow());

        (await _f.Remove.HandleAsync(new RemoveBasketMembersCommand(Guid.NewGuid(), new RemoveMembersInput([Guid.NewGuid()]), BasketFixture.Owner), Ct)).Error.Code.ShouldBe("baskets.not_found");
        (await _f.RemoveAsync(archived, Guid.NewGuid())).Error.Code.ShouldBe("baskets.archived");
        (await _f.Remove.HandleAsync(new RemoveBasketMembersCommand(basket.Id, new RemoveMembersInput([]), BasketFixture.Owner), Ct)).Error.Code.ShouldBe("baskets.invalid");
    }

    [Fact]
    public async Task Reordering_numbers_the_members_from_zero_in_the_given_order_and_audits_the_count()
    {
        var basket = _f.Basket("Core");
        var ids = _f.Instruments("NSE:AAA", "NSE:BBB", "NSE:CCC");
        await _f.AddOkAsync(basket, ids);
        _f.Master.Events.Clear();

        var result = await _f.ReorderMembersAsync(basket, ids[2], ids[0], ids[1]);

        result.IsSuccess.ShouldBeTrue();
        _f.Data.ActiveInstruments(basket).ShouldBe([ids[2], ids[0], ids[1]]);
        _f.Data.Members.Select(m => m.Position).Order().ShouldBe([0, 1, 2]);
        var audit = _f.Master.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Detail).ShouldBe((AccountEventType.BasketMembersReordered, "Reordered the 3 instruments of basket 'Core' (3 moved)"));
    }

    [Fact]
    public async Task Reordering_to_the_current_order_saves_and_audits_nothing()
    {
        var basket = _f.Basket("Core");
        var ids = _f.Instruments("NSE:AAA", "NSE:BBB");
        await _f.AddOkAsync(basket, ids);
        _f.Master.Events.Clear();
        var saves = _f.Data.Saves;
        var revision = basket.Revision;

        var result = await _f.ReorderMembersAsync(basket, ids);

        result.IsSuccess.ShouldBeTrue();
        (_f.Data.Saves, basket.Revision).ShouldBe((saves, revision));
        _f.Master.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_member_order_that_is_not_exactly_the_current_members_is_rejected_and_changes_nothing()
    {
        var basket = _f.Basket("Core");
        var ids = _f.Instruments("NSE:AAA", "NSE:BBB", "NSE:CCC");
        await _f.AddOkAsync(basket, ids[0], ids[1]);
        await _f.RemoveAsync(basket, ids[1]);
        var before = _f.Data.Saves;

        foreach (var order in new[] { Array.Empty<Guid>(), [ids[0], ids[2]], [ids[0], ids[1]], [ids[0], ids[0]], [ids[0], Guid.Empty] })
        {
            var result = await _f.ReorderMembersAsync(basket, order);

            result.Error.Code.ShouldBe("baskets.invalid");
            result.Error.Fields!.Single().Field.ShouldBe("instrumentIds");
        }

        _f.Data.Saves.ShouldBe(before);
        _f.Data.ActiveInstruments(basket).ShouldBe([ids[0]]);
    }

    [Fact]
    public async Task An_empty_basket_accepts_the_empty_order()
    {
        var basket = _f.Basket("Core");

        (await _f.ReorderMembersAsync(basket)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_member_order_whose_basket_changed_between_two_tries_is_a_conflict_not_a_bad_request()
    {
        var basket = _f.Basket("Core");
        var ids = _f.Instruments("NSE:AAA", "NSE:BBB", "NSE:CCC");
        await _f.AddOkAsync(basket, ids[0], ids[1]);
        _f.Data.Collisions.Enqueue(() => _f.Data.AddMember(basket, ids[2], _f.Clock.GetUtcNow()));

        var result = await _f.ReorderMembersAsync(basket, ids[1], ids[0]);

        result.Error.Code.ShouldBe("baskets.conflict");
    }
}
